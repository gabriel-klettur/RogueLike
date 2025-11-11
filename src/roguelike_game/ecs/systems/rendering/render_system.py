import pygame
import time
from roguelike_game.ecs.components.transform.scale import Scale
from roguelike_engine.config.config_z_layer import DEFAULT_Z
from roguelike_game.ecs.systems.fsm.states.death_state import DeathState
import roguelike_engine.config.config as config

try:
    import numpy as np
    from pygame import surfarray
    HAS_NUMPY = True
except ImportError:
    HAS_NUMPY = False

if HAS_NUMPY:
    _RL_LUT_R = (np.arange(256, dtype=np.uint16) * 77).astype(np.uint16)
    _RL_LUT_G = (np.arange(256, dtype=np.uint16) * 150).astype(np.uint16)
    _RL_LUT_B = (np.arange(256, dtype=np.uint16) * 29).astype(np.uint16)

class RenderSystem:
    """
    Sistema responsable de renderizar todas las entidades con Sprite,
    usando culling y orden Z/Y para simular profundidad.
    """

    def __init__(self, screen):
        """
        Inicializa el RenderSystem.

        Args:
            screen (pygame.Surface): Superficie principal de renderizado.
        """
        self.screen = screen
        # Cache de sprites escalados: clave = (entity_id, id(surface), scale_factor)
        self._scaled_sprite_cache: dict[tuple[int, int, float], pygame.Surface] = {}
        # Cache de sprites TEÑIDOS: clave = (id(surface_escalada), r, g, b)
        # Evita recomputar el tinte cada frame para el mismo frame/escala
        self._tinted_cache: dict[tuple[int, int, int, int], pygame.Surface] = {}
        # Rect reutilizable para culling y blit
        self._blit_rect = pygame.Rect(0, 0, 0, 0)
        self._gray_tmp16 = None
        self._gray_tmp16_b = None
        self._gray_shape = (0, 0)
        self._half_surface = None
        self._half_shape = (0, 0)

    def _ensure_gray_tmps(self, w: int, h: int):
        if not HAS_NUMPY:
            return
        if self._gray_tmp16 is None or self._gray_shape != (w, h):
            self._gray_tmp16 = np.empty((w, h), dtype=np.uint16)
            self._gray_tmp16_b = np.empty((w, h), dtype=np.uint16)
            self._gray_shape = (w, h)

    def _ensure_half_surface(self, w: int, h: int):
        if self._half_surface is None or self._half_shape != (w, h):
            self._half_surface = pygame.Surface((w, h), pygame.SRCALPHA, 32)
            self._half_shape = (w, h)

    def update(self, world, screen, camera):
        """
        Calcula y dibuja todos los sprites visibles, ordenados por capa Z y eje Y.

        Pasos:
        1. Determinar el rectángulo de mundo visible (viewport) usando la cámara.
        2. Filtrar entidades que tengan Position y Sprite dentro de ese viewport.
        3. Ordenar esas entidades por (ZLayer, posición Y).
        4. Para cada entidad:
           a. Calcular escala combinada (zoom de cámara × scale del componente).
           b. Obtener o generar sprite escalado usando cache.
           c. Convertir posición de mundo a pantalla.
           d. Aplicar culling de pantalla.
           e. Acumular operaciones de blit.
        5. Ejecutar todas las operaciones de blit en batch para eficiencia.
        """
        # 1) Preparar parámetros de viewport y culling
        screen_rect = screen.get_rect()
        sw, sh = screen_rect.size
        zoom = camera.zoom
        world_left = camera.offset_x
        world_top = camera.offset_y
        world_w = sw / zoom
        world_h = sh / zoom
        world_rect = pygame.Rect(world_left, world_top, world_w, world_h)

        # 2) Acceso rápido a componentes
        comps       = world.components
        pos_map     = comps['Position']
        sprite_map  = comps['Sprite']
        z_map       = comps.get('ZLayer', {})
        scale_map   = comps.get('Scale', {})
        puddle_map  = comps.get('PuddleComponent', {})

        # 3) Filtrar entidades visibles en el viewport
        visible_eids = [
            eid for eid, pos in pos_map.items()
            if (eid in sprite_map)
               and (eid not in puddle_map)
               and world_rect.collidepoint(pos.x, pos.y)
        ]

        # 4) Ordenar entidades por capa Z (fallback DEFAULT_Z) y posición Y
        visible_eids.sort(key=lambda eid: (
            z_map[eid].layer if eid in z_map else DEFAULT_Z,
            pos_map[eid].y
        ))

        camapply = camera.apply
        zoom_key = round(zoom, 2)

        blit_ops: list[tuple[pygame.Surface, tuple[int,int]]] = []

        # 5) Procesar cada entidad para preparar blit
        for eid in visible_eids:
            # Omitir entidades en DeathState para no dibujar sprite normal
            npc_state = world.components['NPCState'].get(eid)
            if npc_state and isinstance(npc_state.fsm.current_state, DeathState):
                continue

            pos    = pos_map[eid]
            sprite = sprite_map[eid]

            # 5a) Calcular scale_factor: zoom de cámara × scale del componente
            entity_scale = scale_map.get(eid, Scale()).scale
            scale_factor = entity_scale * zoom_key

            # 5b) Obtener sprite escalado del cache o generarlo
            # IMPORTANTE: incluir id(surface) para evitar usar un frame antiguo tras cambiar de animación
            orig_surface = sprite.image
            if scale_factor != 1.0:
                key = (eid, id(orig_surface), scale_factor)
                cache = self._scaled_sprite_cache
                if key not in cache:
                    w, h = orig_surface.get_size()
                    # Usar rotozoom para mejor performance y suavidad
                    cache[key] = pygame.transform.rotozoom(orig_surface, 0, scale_factor)
                image = cache[key]
            else:
                image = orig_surface

            # 5c) Convertir posición de mundo a pantalla
            dest = camapply((pos.x, pos.y))

            # 5d) Culling: saltar sprites fuera de la pantalla
            self._blit_rect.size = image.get_size()
            self._blit_rect.topleft = dest
            if not screen_rect.colliderect(self._blit_rect):
                continue

            # 5e) Aplicar tinte si es el jugador en godmode
            try:
                is_player = eid == getattr(world, 'player_entity', None)
                godmode = bool(getattr(getattr(world, 'state', None), 'godmode', False))
                if is_player and godmode:
                    color = (255, 230, 100)
                    tkey = (id(image), color[0], color[1], color[2])
                    tinted = self._tinted_cache.get(tkey)
                    if tinted is None:
                        tinted = self._tint_surface(image, color)
                        self._tinted_cache[tkey] = tinted
                    image = tinted
            except Exception:
                # En caso de cualquier problema con el tinte, continuamos sin romper el render
                pass

            # 5f) Acumular operación de blit
            blit_ops.append((image, dest))

        # 6) Ejecutar todos los blits en batch (más eficiente que múltiples blit individuales)
        if blit_ops:
            screen.blits(blit_ops)


    def _tint_surface(self, surface: pygame.Surface, color: tuple[int, int, int]) -> pygame.Surface:
        """
        Devuelve una copia teñida de 'surface'.
        Si hay NumPy: recolorea por luminancia al tono 'color' (monocromo amarillo),
        preservando alpha y relieve (más visible que un simple MULT).
        Si no: aplica un tinte por blending manteniendo alpha.
        """
        try:
            if HAS_NUMPY:
                # 1) Leer RGB y Alpha del source
                rgb = surfarray.array3d(surface).astype('float32')  # shape (w,h,3)
                # Luminancia perceptual
                lum = (0.299 * rgb[:, :, 0] + 0.587 * rgb[:, :, 1] + 0.114 * rgb[:, :, 2])
                # Normalizar color objetivo (amarillo)
                r_fac = color[0] / 255.0
                g_fac = color[1] / 255.0
                b_fac = color[2] / 255.0
                # 2) Construir nuevo RGB monocromo al tono deseado
                new_rgb = np.zeros_like(rgb)
                new_rgb[:, :, 0] = np.clip(lum * r_fac, 0, 255)
                new_rgb[:, :, 1] = np.clip(lum * g_fac, 0, 255)
                new_rgb[:, :, 2] = np.clip(lum * b_fac, 0, 255)
                new_rgb = new_rgb.astype('uint8')
                # 3) Crear surface destino y blitear RGB
                out = pygame.Surface(surface.get_size(), pygame.SRCALPHA)
                surfarray.blit_array(out, new_rgb)
                # 4) Copiar alpha del source
                try:
                    src_a = surfarray.array_alpha(surface)
                    dst_a = surfarray.pixels_alpha(out)
                    dst_a[:, :] = src_a
                    del dst_a
                except Exception:
                    pass
                return out
            else:
                # Fallback: blending multiplicativo + aditivo (menos intenso que el método con NumPy)
                img = surface.copy()
                tint = pygame.Surface(img.get_size(), pygame.SRCALPHA)
                tint.fill((color[0], color[1], color[2], 255))
                img.blit(tint, (0, 0), special_flags=pygame.BLEND_RGBA_MULT)
                boost = pygame.Surface(img.get_size(), pygame.SRCALPHA)
                boost.fill((40, 35, 0, 0))
                img.blit(boost, (0, 0), special_flags=pygame.BLEND_RGBA_ADD)
                return img
        except Exception:
            return surface

    def apply_grayscale(self, surface, perf_log=None):
        """Convierte la superficie entera a escala de grises con métricas finas opcionales."""
        if HAS_NUMPY:
            sw, sh = surface.get_size()
            mode = getattr(config, 'GRAYSCALE_LUT_MODE', 'index')
            if getattr(config, 'GRAYSCALE_HALF_RES', False):
                hs_w = max(1, sw // 2)
                hs_h = max(1, sh // 2)
                tS0 = time.perf_counter()
                self._ensure_half_surface(hs_w, hs_h)
                pygame.transform.smoothscale(surface, (hs_w, hs_h), self._half_surface)
                tS1 = time.perf_counter()

                t0 = time.perf_counter()
                rgb = surfarray.pixels3d(self._half_surface)
                t1 = time.perf_counter()
                if mode == 'index':
                    # Direct indexing LUT path (fast default)
                    lr = _RL_LUT_R[rgb[:, :, 0]]
                    lg = _RL_LUT_G[rgb[:, :, 1]]
                    lb = _RL_LUT_B[rgb[:, :, 2]]
                    lum16 = (lr + lg + lb) >> 8
                else:
                    # out=/take path with reusable buffers
                    self._ensure_gray_tmps(hs_w, hs_h)
                    np.take(_RL_LUT_R, rgb[:, :, 0], out=self._gray_tmp16)
                    np.take(_RL_LUT_G, rgb[:, :, 1], out=self._gray_tmp16_b)
                    np.add(self._gray_tmp16, self._gray_tmp16_b, out=self._gray_tmp16, dtype=np.uint16)
                    np.take(_RL_LUT_B, rgb[:, :, 2], out=self._gray_tmp16_b)
                    np.add(self._gray_tmp16, self._gray_tmp16_b, out=self._gray_tmp16, dtype=np.uint16)
                    np.right_shift(self._gray_tmp16, 8, out=self._gray_tmp16)
                    lum16 = self._gray_tmp16
                t2 = time.perf_counter()
                rgb[:, :, 0] = lum16
                rgb[:, :, 1] = lum16
                rgb[:, :, 2] = lum16
                t3 = time.perf_counter()

                tS2 = time.perf_counter()
                pygame.transform.smoothscale(self._half_surface, (sw, sh), surface)
                tS3 = time.perf_counter()
                try:
                    if perf_log is not None:
                        perf_log.setdefault("4.Grayscale.half.scale_down", []).append(tS1 - tS0)
                        perf_log.setdefault("4.Grayscale.a pixels3d", []).append(t1 - t0)
                        perf_log.setdefault("4.Grayscale.b luminance", []).append(t2 - t1)
                        perf_log.setdefault("4.Grayscale.e writeback", []).append(t3 - t2)
                        perf_log.setdefault("4.Grayscale.half.scale_up", []).append(tS3 - tS2)
                except Exception:
                    pass
            else:
                t0 = time.perf_counter()
                rgb = surfarray.pixels3d(surface)
                t1 = time.perf_counter()
                if mode == 'index':
                    lr = _RL_LUT_R[rgb[:, :, 0]]
                    lg = _RL_LUT_G[rgb[:, :, 1]]
                    lb = _RL_LUT_B[rgb[:, :, 2]]
                    lum16 = (lr + lg + lb) >> 8
                else:
                    self._ensure_gray_tmps(sw, sh)
                    np.take(_RL_LUT_R, rgb[:, :, 0], out=self._gray_tmp16)
                    np.take(_RL_LUT_G, rgb[:, :, 1], out=self._gray_tmp16_b)
                    np.add(self._gray_tmp16, self._gray_tmp16_b, out=self._gray_tmp16, dtype=np.uint16)
                    np.take(_RL_LUT_B, rgb[:, :, 2], out=self._gray_tmp16_b)
                    np.add(self._gray_tmp16, self._gray_tmp16_b, out=self._gray_tmp16, dtype=np.uint16)
                    np.right_shift(self._gray_tmp16, 8, out=self._gray_tmp16)
                    lum16 = self._gray_tmp16
                t2 = time.perf_counter()
                rgb[:, :, 0] = lum16
                rgb[:, :, 1] = lum16
                rgb[:, :, 2] = lum16
                t3 = time.perf_counter()
                try:
                    if perf_log is not None:
                        perf_log.setdefault("4.Grayscale.a pixels3d", []).append(t1 - t0)
                        perf_log.setdefault("4.Grayscale.b luminance", []).append(t2 - t1)
                        perf_log.setdefault("4.Grayscale.e writeback", []).append(t3 - t2)
                except Exception:
                    pass
        else:
            t0 = time.perf_counter()
            pixels = pygame.PixelArray(surface)
            w, h = surface.get_size()
            for x in range(w):
                for y in range(h):
                    color = surface.unmap_rgb(pixels[x, y])
                    lum = int(0.299*color.r + 0.587*color.g + 0.114*color.b)