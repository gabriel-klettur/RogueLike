import math
import time
import pygame
import roguelike_engine.config.config as config
from roguelike_game.ecs.utils.collider_utils import build_collider_rect
from roguelike_game.ecs.components.physics.mask_collider import MaskCollider


class SpellCollisionDebugSystem:
    """
    Draws visual debug overlays for spell collisions when DEBUG is enabled (F9).
    Covers:
      - Fireballs: projectile point, colliding NPC collider highlight, solid tile hit.
      - Hitboxes: highlight already-hit targets.
      - Laser beams: show beam line with thickness and mark intersecting targets.
      - Auras: draw caster radius.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        self._cross_cache = {}
        # Persistent hit markers (fade out)
        self._markers = []  # each: {kind:'rect'|'circle'|'poly', data:..., color:(r,g,b), label:str, t_end:float}
        # Dedupe trackers
        self._seen_hitbox_pairs = set()  # (hb_eid, target_eid)
        self._laser_prev_pairs = set()   # pairs from previous frame: (caster, target)
        # Font cache for labels
        self._font = None

    # ---------- marker helpers ----------
    def _ensure_font(self):
        if self._font is None:
            try:
                self._font = pygame.font.SysFont(None, 14)
            except Exception:
                self._font = None

    def _add_marker_circle(self, x, y, radius, color, label, duration=10.0):
        self._markers.append({
            'kind': 'circle',
            'data': (float(x), float(y), float(radius)),
            'color': color,
            'label': label,
            't_end': time.time() + duration,
        })

    def _add_marker_rect(self, rect, color, label, duration=10.0):
        self._markers.append({
            'kind': 'rect',
            'data': (float(rect.x), float(rect.y), float(rect.width), float(rect.height)),
            'color': color,
            'label': label,
            't_end': time.time() + duration,
        })

    def _add_marker_poly(self, points_world, color, label, duration=10.0):
        # points_world: sequence of (x,y) in world coordinates
        self._markers.append({
            'kind': 'poly',
            'data': [(float(x), float(y)) for x, y in points_world],
            'color': color,
            'label': label,
            't_end': time.time() + duration,
        })

    def _mask_outline_world(self, mask: pygame.Mask, world_x: float, world_y: float):
        # Convert mask.outline() points to world coordinates based on top-left world position
        outline = mask.outline() or []
        return [(world_x + px, world_y + py) for (px, py) in outline]

    def _render_markers(self, screen, camera):
        now = time.time()
        # cull expired
        self._markers = [m for m in self._markers if m['t_end'] > now]
        if not self._markers:
            return
        self._ensure_font()
        overlay = pygame.Surface(screen.get_size(), pygame.SRCALPHA)
        for m in self._markers:
            t_left = max(0.0, m['t_end'] - now)
            frac = min(1.0, t_left / 10.0)
            r, g, b = m['color']
            alpha = int(40 + 180 * frac)
            if m['kind'] == 'circle':
                x, y, rad = m['data']
                sx, sy = camera.apply((x, y))
                rr = max(1, int(rad * camera.zoom))
                pygame.draw.circle(overlay, (r, g, b, alpha), (int(sx), int(sy)), rr, 2)
                if self._font and m.get('label'):
                    txt = self._font.render(m['label'], True, (r, g, b))
                    overlay.blit(txt, (int(sx) + 6, int(sy) - 6))
            elif m['kind'] == 'rect':
                x, y, w, h = m['data']
                sx, sy = camera.apply((x, y))
                sw, sh = camera.scale((w, h))
                pygame.draw.rect(overlay, (r, g, b, alpha), pygame.Rect(int(sx), int(sy), int(sw), int(sh)), 2)
                if self._font and m.get('label'):
                    txt = self._font.render(m['label'], True, (r, g, b))
                    overlay.blit(txt, (int(sx) + 2, int(sy) - 12))
            elif m['kind'] == 'poly':
                pts = m['data']
                if len(pts) >= 2:
                    pts_s = [camera.apply(p) for p in pts]
                    pygame.draw.lines(overlay, (r, g, b, alpha), True, pts_s, 2)
                    if self._font and m.get('label'):
                        lx, ly = pts_s[0]
                        txt = self._font.render(m['label'], True, (r, g, b))
                        overlay.blit(txt, (int(lx) + 2, int(ly) - 12))
        screen.blit(overlay, (0, 0))

    def _draw_cross(self, screen, x, y, color=(255, 255, 0)):
        key = color
        surf = self._cross_cache.get(key)
        if surf is None:
            surf = pygame.Surface((7, 7), flags=pygame.SRCALPHA)
            pygame.draw.line(surf, color, (0, 3), (6, 3))
            pygame.draw.line(surf, color, (3, 0), (3, 6))
            self._cross_cache[key] = surf
        screen.blit(surf, (int(x - 3), int(y - 3)))

    def _draw_pink_hit(self, screen, camera, wx, wy):
        """Draw a high-contrast pink hit marker at world coords (dot + ring)."""
        sx, sy = camera.apply((wx, wy))
        # Filled dot (hot pink)
        pygame.draw.circle(screen, (255, 105, 180), (int(sx), int(sy)), 3)
        # Ring (light pink)
        pygame.draw.circle(screen, (255, 182, 193), (int(sx), int(sy)), 7, 2)

    def update(self, world, screen, camera):
        if not getattr(config, 'DEBUG', False):
            return
        comps = world.components
        positions = comps.get('Position', {})

        # 1) Fireballs
        fireballs = comps.get('FireballComponent', {})
        multi_map = comps.get('MultiCollider', {})
        for fid, fcmp in fireballs.items():
            pos = positions.get(fid)
            if not pos:
                continue
            # projectile point
            sx, sy = camera.apply((pos.x, pos.y))
            pygame.draw.circle(screen, (255, 80, 80), (int(sx), int(sy)), 3)
            self._draw_cross(screen, sx, sy, (255, 255, 0))
            # Entity collider highlight if colliding now
            # If target has any MaskCollider, ONLY test/draw mask collisions (no rect fallback).
            # If target has no mask, fallback to rects.
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
                    # Only test mask colliders
                    for collider in multi.colliders.values():
                        if not isinstance(collider, MaskCollider):
                            continue
                        bx = tpos.x + collider.offset_x
                        by = tpos.y + collider.offset_y
                        lx = int(pos.x - bx)
                        ly = int(pos.y - by)
                        mw, mh = collider.mask.get_size()
                        if 0 <= lx < mw and 0 <= ly < mh and collider.mask.get_at((lx, ly)):
                            # draw outline of mask
                            outline_pts_w = self._mask_outline_world(collider.mask, bx, by)
                            pts_s = [camera.apply(p) for p in outline_pts_w]
                            if len(pts_s) >= 2:
                                pygame.draw.lines(screen, (255, 255, 0), True, pts_s, 2)
                            # persist as polygon marker
                            self._add_marker_poly(outline_pts_w, (255, 255, 0), f"FB {fid}->{tid}")
                            # draw pink hit marker and persist a small ring marker
                            self._draw_pink_hit(screen, camera, pos.x, pos.y)
                            self._add_marker_circle(pos.x, pos.y, max(4.0, 7.0 / max(camera.zoom, 0.001)), (255, 105, 180), f"FB HIT {fid}->{tid}")
                            # draw exact mask-bit pixel center marker (magenta) for proof
                            bit_wx = bx + lx + 0.5
                            bit_wy = by + ly + 0.5
                            bsx, bsy = camera.apply((bit_wx, bit_wy))
                            pygame.draw.circle(screen, (255, 0, 255), (int(bsx), int(bsy)), 2)
                            hit_drawn = True
                            break
                else:
                    # No mask colliders: fallback to rects
                    for collider in multi.colliders.values():
                        if isinstance(collider, MaskCollider):
                            continue
                        rect_w = build_collider_rect(tpos.x, tpos.y, collider)
                        if rect_w.collidepoint(pos.x, pos.y):
                            rsx, rsy = camera.apply((rect_w.x, rect_w.y))
                            rect_s = pygame.Rect(int(rsx), int(rsy), int(rect_w.width), int(rect_w.height))
                            pygame.draw.rect(screen, (255, 255, 0), rect_s, 2)
                            # persist marker (world-space)
                            self._add_marker_rect(rect_w, (255, 255, 0), f"FB {fid}->{tid}")
                            # draw pink hit marker and persist a small ring marker
                            self._draw_pink_hit(screen, camera, pos.x, pos.y)
                            self._add_marker_circle(pos.x, pos.y, max(4.0, 7.0 / max(camera.zoom, 0.001)), (255, 105, 180), f"FB HIT {fid}->{tid}")
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
                # mark small rect at point in world coordinates
                self._add_marker_rect(pygame.Rect(pos.x - 3, pos.y - 3, 6, 6), (255, 140, 0), f"FB {fid}->TILE")

        # 2) Hitboxes: mark already-hit targets
        hb_store = comps.get('HitboxComponent', {})
        for eid, hb in hb_store.items():
            for tid in getattr(hb, 'hit_targets', set()):
                tpos = positions.get(tid)
                multi = multi_map.get(tid)
                if not (tpos and multi):
                    continue
                # outline all colliders of the target in red (prefer mask outline)
                pair = (eid, tid)
                is_new = pair not in self._seen_hitbox_pairs
                if is_new:
                    self._seen_hitbox_pairs.add(pair)
                for collider in multi.colliders.values():
                    if isinstance(collider, MaskCollider):
                        bx = tpos.x + collider.offset_x
                        by = tpos.y + collider.offset_y
                        outline_pts_w = self._mask_outline_world(collider.mask, bx, by)
                        pts_s = [camera.apply(p) for p in outline_pts_w]
                        if len(pts_s) >= 2:
                            pygame.draw.lines(screen, (255, 0, 0), True, pts_s, 2)
                        if is_new:
                            self._add_marker_poly(outline_pts_w, (255, 0, 0), f"HB {eid}->{tid}")
                    else:
                        rect_w = build_collider_rect(tpos.x, tpos.y, collider)
                        rsx, rsy = camera.apply((rect_w.x, rect_w.y))
                        rect_s = pygame.Rect(int(rsx), int(rsy), int(rect_w.width), int(rect_w.height))
                        pygame.draw.rect(screen, (255, 0, 0), rect_s, 2)
                        if is_new:
                            self._add_marker_rect(rect_w, (255, 0, 0), f"HB {eid}->{tid}")

        # 3) Laser beams: draw beam and mark intersecting targets
        beams = comps.get('LaserBeamComponent', {})
        laser_curr_pairs = set()
        for caster, beam in beams.items():
            x1, y1 = beam.origin
            x2, y2 = beam.target
            dx = x2 - x1
            dy = y2 - y1
            length = math.hypot(dx, dy) or 1
            thickness_px = max(2, int(getattr(beam, 'scale', 1.0) * 20))
            color_line = (120, 200, 255, 160)
            # draw line in screen space (convert endpoints)
            p1 = camera.apply((x1, y1))
            p2 = camera.apply((x2, y2))
            line_surf = pygame.Surface(screen.get_size(), pygame.SRCALPHA)
            pygame.draw.line(line_surf, color_line, p1, p2, thickness_px)
            screen.blit(line_surf, (0, 0))
            half_th = thickness_px / 2
            # mark intersecting targets (prefer collider shape)
            for tid in world.get_entities_with('Position', 'Health'):
                if tid == caster:
                    continue
                tpos = positions.get(tid)
                # Prefer MultiCollider if available
                multi = comps.get('MultiCollider', {}).get(tid)
                hit_found = False
                if multi:
                    # Sample along beam and across thickness; test mask first, then rect fallback
                    steps = max(8, int(length / 16))
                    nx, ny = (-dy / length, dx / length)  # normal
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
                                # sample across thickness every 3 px
                                off = 0
                                while off <= half_th:
                                    for sgn in (-1, 1):
                                        px = cx + nx * off * sgn
                                        py = cy + ny * off * sgn
                                        lx = int(px - bx)
                                        ly = int(py - by)
                                        if 0 <= lx < mw and 0 <= ly < mh and collider.mask.get_at((lx, ly)):
                                            hit_found = True
                                            outline_pts_w = self._mask_outline_world(collider.mask, bx, by)
                                            pts_s = [camera.apply(p) for p in outline_pts_w]
                                            if len(pts_s) >= 2:
                                                pygame.draw.lines(screen, (255, 255, 0), True, pts_s, 2)
                                            break
                                    if hit_found:
                                        break
                                    off += 3
                        else:
                            rect_w = build_collider_rect(tpos.x, tpos.y, collider)
                            # quick AABB vs expanded segment bbox check
                            seg_min_x = min(x1, x2) - half_th
                            seg_max_x = max(x1, x2) + half_th
                            seg_min_y = min(y1, y2) - half_th
                            seg_max_y = max(y1, y2) + half_th
                            if rect_w.right < seg_min_x or rect_w.left > seg_max_x or rect_w.bottom < seg_min_y or rect_w.top > seg_max_y:
                                pass
                            else:
                                # project rect center to beam; distance check
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
                    # Fallback: original circle approximation around sprite center
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
                    if pair not in self._laser_prev_pairs:
                        # Persist shape marker if possible
                        multi = comps.get('MultiCollider', {}).get(tid)
                        if multi:
                            # prefer first mask collider for marker
                            mc = next((c for c in multi.colliders.values() if isinstance(c, MaskCollider)), None)
                            if mc is not None:
                                bx = tpos.x + mc.offset_x
                                by = tpos.y + mc.offset_y
                                outline_pts_w = self._mask_outline_world(mc.mask, bx, by)
                                self._add_marker_poly(outline_pts_w, (255, 255, 0), f"LAS {caster}->{tid}")
                            else:
                                # fallback rect of first collider
                                anyc = next(iter(multi.colliders.values()))
                                rect_w = build_collider_rect(tpos.x, tpos.y, anyc)
                                self._add_marker_rect(rect_w, (255, 255, 0), f"LAS {caster}->{tid}")
                        else:
                            # fallback: small circle at position
                            self._add_marker_circle(tpos.x, tpos.y, 8, (255, 255, 0), f"LAS {caster}->{tid}")
        # update laser prev set for next frame
        self._laser_prev_pairs = laser_curr_pairs

        # 4) Auras: draw radius
        auras = comps.get('AuraComponent', {})
        for caster, aura in auras.items():
            pos = positions.get(caster)
            if not pos:
                continue
            r = getattr(aura, 'radius', 0)
            if r <= 0:
                continue
            cx, cy = camera.apply((pos.x, pos.y))
            # approximate circle in screen space with current zoom
            rr = int(r * camera.zoom)
            if rr > 0:
                pygame.draw.circle(screen, (0, 200, 255), (int(cx), int(cy)), rr, 1)

        # 5) Consume gameplay debug events (e.g., Fireball hits, Hitbox hits) to ensure outline even if source was removed
        dbg = comps.get('DebugSpellHits', {})
        queue = dbg.get('_queue', []) if isinstance(dbg, dict) else []
        if queue:
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
                    # Prefer mask collider for outline
                    mc = next((c for c in multi.colliders.values() if isinstance(c, MaskCollider)), None)
                    if mc is not None:
                        bx = tpos.x + mc.offset_x
                        by = tpos.y + mc.offset_y
                        outline_pts_w = self._mask_outline_world(mc.mask, bx, by)
                        # draw now
                        pts_s = [camera.apply(p) for p in outline_pts_w]
                        if len(pts_s) >= 2:
                            pygame.draw.lines(screen, (255, 255, 0), True, pts_s, 2)
                        # persist
                        self._add_marker_poly(outline_pts_w, (255, 255, 0), f"FB {src}->{tid}")
                    else:
                        # fallback first collider rect
                        anyc = next(iter(multi.colliders.values()))
                        rect_w = build_collider_rect(tpos.x, tpos.y, anyc)
                        rsx, rsy = camera.apply((rect_w.x, rect_w.y))
                        rect_s = pygame.Rect(int(rsx), int(rsy), int(rect_w.width), int(rect_w.height))
                        pygame.draw.rect(screen, (255, 255, 0), rect_s, 2)
                        self._add_marker_rect(rect_w, (255, 255, 0), f"FB {src}->{tid}")
                    # draw pink hit point from event position if provided
                    if isinstance(ev_pos, (list, tuple)) and len(ev_pos) == 2:
                        hx, hy = float(ev_pos[0]), float(ev_pos[1])
                        self._draw_pink_hit(screen, camera, hx, hy)
                        self._add_marker_circle(hx, hy, max(4.0, 7.0 / max(camera.zoom, 0.001)), (255, 105, 180), f"FB HIT {src}->{tid}")
                        # if we still have mc, overlay the exact mask-bit center for proof
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
                    # Hitbox impact: draw exact hit position and outline the target collider
                    hb_eid = ev.get('hb_eid')
                    tid = ev.get('target')
                    ev_pos = ev.get('pos')
                    tpos = positions.get(tid)
                    multi = comps.get('MultiCollider', {}).get(tid)
                    if not (tpos and multi):
                        continue
                    # Prefer mask collider for outline
                    mc = next((c for c in multi.colliders.values() if isinstance(c, MaskCollider)), None)
                    if mc is not None:
                        bx = tpos.x + mc.offset_x
                        by = tpos.y + mc.offset_y
                        outline_pts_w = self._mask_outline_world(mc.mask, bx, by)
                        pts_s = [camera.apply(p) for p in outline_pts_w]
                        if len(pts_s) >= 2:
                            pygame.draw.lines(screen, (255, 0, 0), True, pts_s, 2)
                        # persist red outline marker
                        self._add_marker_poly(outline_pts_w, (255, 0, 0), f"HB {hb_eid}->{tid}")
                    else:
                        # fallback first collider rect
                        anyc = next(iter(multi.colliders.values()))
                        rect_w = build_collider_rect(tpos.x, tpos.y, anyc)
                        rsx, rsy = camera.apply((rect_w.x, rect_w.y))
                        rect_s = pygame.Rect(int(rsx), int(rsy), int(rect_w.width), int(rect_w.height))
                        pygame.draw.rect(screen, (255, 0, 0), rect_s, 2)
                        self._add_marker_rect(rect_w, (255, 0, 0), f"HB {hb_eid}->{tid}")
                    # draw pink hit point from event position if provided
                    if isinstance(ev_pos, (list, tuple)) and len(ev_pos) == 2:
                        hx, hy = float(ev_pos[0]), float(ev_pos[1])
                        self._draw_pink_hit(screen, camera, hx, hy)
                        self._add_marker_circle(hx, hy, max(4.0, 7.0 / max(camera.zoom, 0.001)), (255, 105, 180), f"HB HIT {hb_eid}->{tid}")
            # clear after consuming
            queue.clear()

        # Render persistent markers last
        self._render_markers(screen, camera)
