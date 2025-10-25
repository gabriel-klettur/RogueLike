import pygame
import math
from roguelike_engine.utils.benchmark import benchmark

class ParticleRenderSystem:
    """
    ECS system to render particles: dibuja cada partícula como un círculo.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log
        self._texture_cache: dict[str, pygame.Surface] = {}
        self._frame_cache: dict[tuple[str, int, int], pygame.Surface] = {}
    
    def update(self, world, screen, camera):
        particles = world.components.get('ParticleComponent', {})
        positions = world.components.get('Position', {})
        # Local helpers to evaluate curves safely
        def _eval_curve(curve, t: float, default: float) -> float:
            try:
                if not isinstance(curve, (list, tuple)) or len(curve) == 0:
                    return float(default)
                pts = []
                for pt in curve:
                    pts.append((float(pt[0]), float(pt[1])))
                if not pts:
                    return float(default)
                pts.sort(key=lambda x: x[0])
                if t <= pts[0][0]:
                    return pts[0][1]
                if t >= pts[-1][0]:
                    return pts[-1][1]
                for i in range(1, len(pts)):
                    t0, v0 = pts[i-1]
                    t1, v1 = pts[i]
                    if t0 <= t <= t1 and t1 > t0:
                        k = (t - t0) / (t1 - t0)
                        return v0 * (1 - k) + v1 * k
            except Exception:
                return float(default)
            return float(default)
        def _eval_color_gradient(grad, t: float, base):
            try:
                if not isinstance(grad, (list, tuple)) or len(grad) == 0:
                    return base
                pts = []
                for pt in grad:
                    col = pt[1]
                    if isinstance(col, (list, tuple)) and len(col) >= 3:
                        pts.append((float(pt[0]), (int(col[0]), int(col[1]), int(col[2]))))
                if not pts:
                    return base
                pts.sort(key=lambda x: x[0])
                if t <= pts[0][0]:
                    return pts[0][1]
                if t >= pts[-1][0]:
                    return pts[-1][1]
                for i in range(1, len(pts)):
                    t0, c0 = pts[i-1]
                    t1, c1 = pts[i]
                    if t0 <= t <= t1 and t1 > t0:
                        k = (t - t0) / (t1 - t0)
                        r = int(c0[0] * (1 - k) + c1[0] * k)
                        g = int(c0[1] * (1 - k) + c1[1] * k)
                        b = int(c0[2] * (1 - k) + c1[2] * k)
                        return (r, g, b)
            except Exception:
                return base
            return base
        draw_calls: list[tuple[tuple[int, str], pygame.Surface, tuple[int, int], bool]] = []
        for eid, comp in list(particles.items()):
            pos = positions.get(eid)
            if pos is None:
                continue
            screen_pos = camera.apply((pos.x, pos.y))
            # Edad normalizada 0..1
            t = 0.0
            try:
                t = max(0.0, min(1.0, comp.age / max(1, comp.lifespan)))
            except Exception:
                t = 0.0
            # Tamaño con curva si existe
            base_size = int(getattr(comp, 'base_size', getattr(comp, 'size', 1)))
            if isinstance(getattr(comp, 'size_over_life', None), (list, tuple)):
                scale = max(0.05, _eval_curve(getattr(comp, 'size_over_life'), t, 1.0))
                draw_size = max(1, int(base_size * scale))
            else:
                draw_size = int(getattr(comp, 'size', 1))
            # Alpha con curva si existe
            if isinstance(getattr(comp, 'alpha_over_life', None), (list, tuple)):
                aval = max(0.0, min(1.0, _eval_curve(getattr(comp, 'alpha_over_life'), t, 1.0)))
                alpha = max(0, min(255, int(255.0 * aval)))
            else:
                alpha = int(max(0, 255 * (1 - (comp.age / comp.lifespan))) if comp.lifespan > 0 else 255)
            # Color con gradiente si existe
            base_color = tuple(getattr(comp, 'base_color', getattr(comp, 'color', (255, 255, 255))))
            if isinstance(getattr(comp, 'color_over_life', None), (list, tuple)):
                color = _eval_color_gradient(getattr(comp, 'color_over_life'), t, base_color)
            else:
                color = tuple(getattr(comp, 'color', base_color))
            # Aplicar zoom
            size_z = int(max(1, draw_size * float(getattr(camera, 'zoom', 1.0) or 1.0)))
            x, y = screen_pos
            dst = (int(x - size_z/2), int(y - size_z/2))
            blend_mode = getattr(comp, 'blend_mode', None)
            # Textured path when texture_path is provided
            tex_path = getattr(comp, 'texture_path', None)
            sheet = None
            if isinstance(tex_path, str) and tex_path:
                sheet = self._texture_cache.get(tex_path)
                if sheet is None:
                    try:
                        img = pygame.image.load(tex_path)
                        sheet = img.convert_alpha()
                        self._texture_cache[tex_path] = sheet
                    except Exception:
                        sheet = None
            if sheet is not None:
                fb = getattr(comp, 'flipbook', None)
                if isinstance(fb, dict) and sheet is not None:
                    sw, sh = sheet.get_size()
                    cols = int(fb.get('cols', 1) or 1)
                    rows = int(fb.get('rows', 1) or 1)
                    total = int(fb.get('total', max(1, cols * rows)) or max(1, cols * rows))
                    fw = int(fb.get('frame_w', sw // max(1, cols)))
                    fh = int(fb.get('frame_h', sh // max(1, rows)))
                    loop = bool(fb.get('loop', True))
                    # Selección de frame: usar vida normalizada para recorrer flipbook
                    idx = int(min(0.999, max(0.0, t)) * total)
                    if loop and total > 0:
                        idx = idx % total
                    idx = max(0, min(total - 1, idx)) if total > 0 else 0
                    col = (idx % cols)
                    row = (idx // cols)
                    rx = col * fw
                    ry = row * fh
                    rect = pygame.Rect(rx, ry, fw, fh)
                    # Cache scaled frames per (texture, frame idx, size)
                    cache_key = (tex_path, idx, size_z)
                    frame = self._frame_cache.get(cache_key)
                    if frame is None:
                        try:
                            raw = sheet.subsurface(rect).copy()
                        except Exception:
                            raw = sheet.copy()
                        try:
                            if (raw.get_width(), raw.get_height()) != (size_z, size_z):
                                raw = pygame.transform.smoothscale(raw, (size_z, size_z))
                        except Exception:
                            try:
                                raw = pygame.transform.scale(raw, (size_z, size_z))
                            except Exception:
                                pass
                        frame = raw
                        # Simple cache limit to avoid unbounded growth
                        if len(self._frame_cache) > 512:
                            self._frame_cache.clear()
                        self._frame_cache[cache_key] = frame
                else:
                    # Whole-sheet case: scale once and cache at idx=0
                    cache_key = (tex_path, 0, size_z)
                    frame = self._frame_cache.get(cache_key)
                    if frame is None:
                        raw = sheet.copy()
                        try:
                            if (raw.get_width(), raw.get_height()) != (size_z, size_z):
                                raw = pygame.transform.smoothscale(raw, (size_z, size_z))
                        except Exception:
                            try:
                                raw = pygame.transform.scale(raw, (size_z, size_z))
                            except Exception:
                                pass
                        frame = raw
                        if len(self._frame_cache) > 512:
                            self._frame_cache.clear()
                        self._frame_cache[cache_key] = frame
                # Tint por color_over_life si aplica
                try:
                    if color is not None:
                        tint = pygame.Surface(frame.get_size(), pygame.SRCALPHA)
                        tint.fill((*color, 255))
                        frame.blit(tint, (0, 0), special_flags=pygame.BLEND_MULT)
                except Exception:
                    pass
                # Alpha: support premultiplied_alpha by modulating RGB instead of set_alpha
                bm = str(blend_mode).lower() if isinstance(blend_mode, str) else None
                if bm == 'premultiplied_alpha':
                    try:
                        mod = pygame.Surface(frame.get_size(), pygame.SRCALPHA)
                        mod.fill((alpha, alpha, alpha, 255))
                        frame.blit(mod, (0, 0), special_flags=pygame.BLEND_MULT)
                    except Exception:
                        pass
                else:
                    try:
                        frame.set_alpha(alpha)
                    except Exception:
                        pass
                is_add = 1 if (isinstance(blend_mode, str) and blend_mode.lower() == 'additive') else 0
                draw_calls.append(((is_add, tex_path or ""), frame, dst, bool(is_add)))
            else:
                # Fallback: cuadro coloreado como antes
                surf = pygame.Surface((size_z, size_z), pygame.SRCALPHA)
                surf.fill((*color, alpha))
                is_add = 1 if (isinstance(blend_mode, str) and blend_mode.lower() == 'additive') else 0
                draw_calls.append(((is_add, ""), surf, dst, bool(is_add)))
        # Ribbon (trail) rendering from RibbonComponent (points are sampled elsewhere)
        ribbons = world.components.get('RibbonComponent', {})
        for rid, rib in list(ribbons.items()):
            pts = getattr(rib, 'points', [])
            if not isinstance(pts, list) or len(pts) < 2:
                continue
            base_color = tuple(getattr(rib, 'color', (255, 255, 255)))
            base_alpha = int(max(0, min(255, getattr(rib, 'alpha', 200))))
            width_px = int(max(1, getattr(rib, 'width_px', 6)))
            is_add = 1 if (isinstance(getattr(rib, 'blend_mode', None), str) and getattr(rib, 'blend_mode').lower() == 'additive') else 0
            zoom = float(getattr(camera, 'zoom', 1.0) or 1.0)
            # Iterate segments, oldest to newest, with mild width/alpha falloff
            n = len(pts)
            for i in range(n - 1):
                p0 = pts[i]
                p1 = pts[i + 1]
                dx = (p1.x - p0.x)
                dy = (p1.y - p0.y)
                dist = math.hypot(dx, dy)
                if dist < 0.5:
                    continue
                nx = dx / dist
                ny = dy / dist
                px = -ny
                py = nx
                # Falloff: segments near the tail are thinner and more transparent
                frac = i / max(1, (n - 2))
                w = int(max(1, width_px * zoom * (1.0 - 0.6 * frac)))
                a = int(max(0, min(255, base_alpha * (1.0 - 0.7 * frac))))
                # Build quad in world space
                x0l = p0.x + px * (w * 0.5)
                y0l = p0.y + py * (w * 0.5)
                x0r = p0.x - px * (w * 0.5)
                y0r = p0.y - py * (w * 0.5)
                x1l = p1.x + px * (w * 0.5)
                y1l = p1.y + py * (w * 0.5)
                x1r = p1.x - px * (w * 0.5)
                y1r = p1.y - py * (w * 0.5)
                # To screen space
                s0l = camera.apply((x0l, y0l))
                s0r = camera.apply((x0r, y0r))
                s1l = camera.apply((x1l, y1l))
                s1r = camera.apply((x1r, y1r))
                # Build temp surface around the polygon bounds
                xs = [s0l[0], s0r[0], s1l[0], s1r[0]]
                ys = [s0l[1], s0r[1], s1l[1], s1r[1]]
                minx = int(min(xs))
                miny = int(min(ys))
                maxx = int(max(xs))
                maxy = int(max(ys))
                bw = max(2, maxx - minx + 2)
                bh = max(2, maxy - miny + 2)
                poly = [
                    (int(s0l[0] - minx + 1), int(s0l[1] - miny + 1)),
                    (int(s1l[0] - minx + 1), int(s1l[1] - miny + 1)),
                    (int(s1r[0] - minx + 1), int(s1r[1] - miny + 1)),
                    (int(s0r[0] - minx + 1), int(s0r[1] - miny + 1)),
                ]
                surf = pygame.Surface((bw, bh), pygame.SRCALPHA)
                r, g, b = base_color
                pygame.draw.polygon(surf, (int(r), int(g), int(b), a), poly)
                draw_calls.append(((is_add, "__ribbon__"), surf, (minx, miny), bool(is_add)))
        # Batching: sort by (blend_mode, texture_path) to reduce state changes
        draw_calls.sort(key=lambda it: it[0])
        for _key, surf, dst, is_add in draw_calls:
            if is_add:
                screen.blit(surf, dst, special_flags=pygame.BLEND_ADD)
            else:
                screen.blit(surf, dst)