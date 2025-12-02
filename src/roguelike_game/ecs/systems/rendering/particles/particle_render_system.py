import pygame
from collections import OrderedDict, defaultdict
import math
from roguelike_engine.utils.benchmark.benchmark import benchmark

class ParticleRenderSystem:
    """
    ECS system to render particles: dibuja cada partícula como un círculo.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log
        self._texture_cache: dict[str, pygame.Surface] = {}
        # Cache de frames escalados por (tex_path, frame_idx, size_px)
        self._frame_cache: "OrderedDict[tuple[str, int, int], pygame.Surface]" = OrderedDict()
        # Cache de frames tintados/modulados por (tex_path, frame_idx, size_px, color_q, alpha_q, premul_flag)
        self._tinted_cache: "OrderedDict[tuple[str, int, int, tuple[int,int,int], int, int], pygame.Surface]" = OrderedDict()
        # Cache para fallback (sin textura): (size_px, color_q, alpha_q, is_add)
        self._fallback_cache: "OrderedDict[tuple[int, tuple[int,int,int], int, int], pygame.Surface]" = OrderedDict()
        # Cache de frames base (no escalados ni tintados) por (tex_path, rx, ry, fw, fh)
        self._base_frame_cache: "OrderedDict[tuple[str, int, int, int, int], pygame.Surface]" = OrderedDict()
        self._max_base_frame_cache = 1024
        # Límites para evitar crecimiento sin control
        self._max_frame_cache = 1024
        self._max_tinted_cache = 1024
        self._max_fallback_cache = 512
        # Helpers LRU
        self._lru_get = lambda od, k: (od.move_to_end(k) or od.get(k)) if k in od else None
        def _lru_put(od: OrderedDict, k, v, cap: int):
            od[k] = v
            od.move_to_end(k)
            while len(od) > cap:
                od.popitem(last=False)
        self._lru_put = _lru_put
        # Pool de surfaces para ribbons por tamaño (w,h)
        self._ribbon_pool: dict[tuple[int,int], list[pygame.Surface]] = {}
        self._ribbon_pool_cap_per_size = 8
        # Presupuesto de dibujado por frame para partículas/ribbons
        self.max_draw_calls = 4000
    
    def _get_ribbon_surface(self, w: int, h: int) -> pygame.Surface:
        key = (int(max(1, w)), int(max(1, h)))
        bucket = self._ribbon_pool.get(key)
        if bucket and len(bucket) > 0:
            surf = bucket.pop()
            # limpiar alpha sin recrear surface
            surf.fill((0, 0, 0, 0))
            return surf
        return pygame.Surface(key, pygame.SRCALPHA)
    
    def _recycle_ribbon_surface(self, surf: pygame.Surface):
        try:
            w, h = surf.get_size()
            key = (w, h)
            bucket = self._ribbon_pool.setdefault(key, [])
            if len(bucket) < self._ribbon_pool_cap_per_size:
                bucket.append(surf)
        except Exception:
            pass
    
    def update(self, world, screen, camera):
        particles = world.components.get('ParticleComponent', {})
        positions = world.components.get('Position', {})
        screen_rect = screen.get_rect()
        expanded_screen = screen_rect.inflate(64, 64)
        zoom = float(getattr(camera, 'zoom', 1.0) or 1.0)
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
        # Cuantizadores para reducir combinaciones en cachés
        def _q_color(c: tuple[int,int,int]) -> tuple[int,int,int]:
            try:
                # Reducir gradación a pasos de 16
                return (int(c[0]) // 16 * 16, int(c[1]) // 16 * 16, int(c[2]) // 16 * 16)
            except Exception:
                return (255, 255, 255)
        def _q_alpha(a: int) -> int:
            try:
                return max(0, min(255, int(a) // 16 * 16))
            except Exception:
                return 255
        def _q_size(sz: int) -> int:
            try:
                # cuantizar a múltiplos de 2 px para aumentar hits de caché
                return max(1, int(round(sz / 2.0)) * 2)
            except Exception:
                return max(1, int(sz))

        draw_calls: list[tuple[tuple[int, str], pygame.Surface, tuple[int, int], bool]] = []
        max_calls = int(getattr(self, 'max_draw_calls', 4000) or 4000)
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
            size_z = int(max(1, draw_size * zoom))
            size_z = _q_size(size_z)
            x, y = screen_pos
            dst = (int(x - size_z/2), int(y - size_z/2))
            blend_mode = getattr(comp, 'blend_mode', None)
            # Culling por pantalla (rect aproximado de la partícula)
            pr = pygame.Rect(dst[0], dst[1], size_z, size_z)
            if not pr.colliderect(expanded_screen):
                continue
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
                    base_key = (tex_path, idx, size_z)
                    frame = self._lru_get(self._frame_cache, base_key)
                    if frame is None:
                        # Obtener frame base (sin escalar) del flipbook una sola vez
                        rect_key = (tex_path, rect.x, rect.y, rect.w, rect.h)
                        base_frame = self._lru_get(self._base_frame_cache, rect_key)
                        if base_frame is None:
                            try:
                                base_raw = sheet.subsurface(rect).copy()
                            except Exception:
                                base_raw = sheet.copy()
                            base_frame = base_raw.convert_alpha() if hasattr(base_raw, 'convert_alpha') else base_raw
                            self._lru_put(self._base_frame_cache, rect_key, base_frame, self._max_base_frame_cache)
                        # Escalar desde frame base
                        try:
                            if (base_frame.get_width(), base_frame.get_height()) != (size_z, size_z):
                                raw = pygame.transform.smoothscale(base_frame, (size_z, size_z))
                            else:
                                raw = base_frame
                        except Exception:
                            try:
                                raw = pygame.transform.scale(base_frame, (size_z, size_z))
                            except Exception:
                                raw = base_frame
                        frame = raw.convert_alpha() if hasattr(raw, 'convert_alpha') else raw
                        # LRU insert
                        self._lru_put(self._frame_cache, base_key, frame, self._max_frame_cache)
                else:
                    # Whole-sheet case: scale once and cache at idx=0
                    base_key = (tex_path, 0, size_z)
                    frame = self._lru_get(self._frame_cache, base_key)
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
                        frame = raw.convert_alpha() if hasattr(raw, 'convert_alpha') else raw
                        self._lru_put(self._frame_cache, base_key, frame, self._max_frame_cache)
                # Construir frame tintado con caché (no mutar el base frame)
                bm = str(blend_mode).lower() if isinstance(blend_mode, str) else None
                cq = _q_color(color)
                aq = _q_alpha(alpha)
                premul_flag = 1 if bm == 'premultiplied_alpha' else 0
                # Skip si alpha es 0 tras cuantizar
                if aq <= 0:
                    continue
                tint_key = (base_key[0], base_key[1], base_key[2], cq, aq, premul_flag)
                tf = self._lru_get(self._tinted_cache, tint_key)
                if tf is None:
                    try:
                        tf = frame.copy()
                        if color is not None:
                            tint = pygame.Surface(tf.get_size(), pygame.SRCALPHA)
                            tint.fill((*cq, 255))
                            tf.blit(tint, (0, 0), special_flags=pygame.BLEND_MULT)
                        if premul_flag:
                            mod = pygame.Surface(tf.get_size(), pygame.SRCALPHA)
                            mod.fill((aq, aq, aq, 255))
                            tf.blit(mod, (0, 0), special_flags=pygame.BLEND_MULT)
                        else:
                            tf.set_alpha(aq)
                    except Exception:
                        tf = frame
                    self._lru_put(self._tinted_cache, tint_key, tf, self._max_tinted_cache)
                is_add = 1 if (isinstance(blend_mode, str) and blend_mode.lower() == 'additive') else 0
                if len(draw_calls) < max_calls:
                    draw_calls.append(((is_add, tex_path or ""), tf, dst, bool(is_add)))
            else:
                # Fallback: cuadro coloreado como antes
                cq = _q_color(color)
                aq = _q_alpha(alpha)
                if aq <= 0:
                    continue
                is_add = 1 if (isinstance(blend_mode, str) and blend_mode.lower() == 'additive') else 0
                fb_key = (size_z, cq, aq, is_add)
                surf = self._lru_get(self._fallback_cache, fb_key)
                if surf is None:
                    surf = pygame.Surface((size_z, size_z), pygame.SRCALPHA)
                    surf.fill((*cq, aq))
                    self._lru_put(self._fallback_cache, fb_key, surf, self._max_fallback_cache)
                if len(draw_calls) < max_calls:
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
            # Submuestreo de segmentos para limitar blits; máximo ~32 quads por ribbon
            n = len(pts)
            stride = max(1, int((n - 1) / 32))
            # Calcular bounding box de todos los puntos (con zoom aplicado)
            sx_list = []
            sy_list = []
            for p in pts[::stride]:
                sx, sy = camera.apply((p.x, p.y))
                sx_list.append(sx)
                sy_list.append(sy)
            if not sx_list:
                continue
            minx = int(min(sx_list))
            miny = int(min(sy_list))
            maxx = int(max(sx_list))
            maxy = int(max(sy_list))
            # Culling por pantalla global del ribbon
            if (maxx < expanded_screen.left) or (minx > expanded_screen.right) or (maxy < expanded_screen.top) or (miny > expanded_screen.bottom):
                continue
            bw = max(2, maxx - minx + 2)
            bh = max(2, maxy - miny + 2)
            r_surf = self._get_ribbon_surface(bw, bh)
            r, g, b = base_color
            # Dibujar todos los segmentos subsampleados sobre una única surface
            for i in range(0, n - 1, stride):
                p0 = pts[i]
                p1 = pts[min(i + stride, n - 1)]
                dx = (p1.x - p0.x)
                dy = (p1.y - p0.y)
                dist = math.hypot(dx, dy)
                if dist < 0.5:
                    continue
                nx = dx / dist
                ny = dy / dist
                px = -ny
                py = nx
                frac = i / max(1, (n - 2))
                w = int(max(1, width_px * zoom * (1.0 - 0.6 * frac)))
                a = int(max(0, min(255, base_alpha * (1.0 - 0.7 * frac))))
                x0l = p0.x + px * (w * 0.5)
                y0l = p0.y + py * (w * 0.5)
                x1l = p1.x + px * (w * 0.5)
                y1l = p1.y + py * (w * 0.5)
                x1r = p1.x - px * (w * 0.5)
                y1r = p1.y - py * (w * 0.5)
                s0l = camera.apply((x0l, y0l))
                s0r = camera.apply((x0r, y0r))
                s1l = camera.apply((x1l, y1l))
                s1r = camera.apply((x1r, y1r))
                poly = [
                    (int(s0l[0] - minx + 1), int(s0l[1] - miny + 1)),
                    (int(s1l[0] - minx + 1), int(s1l[1] - miny + 1)),
                    (int(s1r[0] - minx + 1), int(s1r[1] - miny + 1)),
                    (int(s0r[0] - minx + 1), int(s0r[1] - miny + 1)),
                ]
                pygame.draw.polygon(r_surf, (int(r), int(g), int(b), a), poly)
            if len(draw_calls) < max_calls:
                draw_calls.append(((is_add, "__ribbon__"), r_surf, (minx, miny), bool(is_add)))
        # Batching: agrupar por (blend_mode, texture_path) para evitar O(N log N) sort
        buckets: dict[tuple[int, str], list[tuple[pygame.Surface, tuple[int,int], bool]]] = defaultdict(list)
        for key, surf, dst, is_add in draw_calls:
            buckets[key].append((surf, dst, is_add))
        # Iterar claves en orden estable (additive primero/segundo según clave)
        for key in sorted(buckets.keys()):
            is_add_bucket = bool(key[0])
            for surf, dst, _ in buckets[key]:
                if is_add_bucket:
                    screen.blit(surf, dst, special_flags=pygame.BLEND_ADD)
                else:
                    screen.blit(surf, dst)
                # Reciclar surfaces de ribbons tras el blit
                if key[1] == "__ribbon__":
                    self._recycle_ribbon_surface(surf)