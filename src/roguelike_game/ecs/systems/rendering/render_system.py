# Path: src/roguelike_game/ecs/systems/rendering/render_system.py
import pygame
from roguelike_game.ecs.components.transform.scale import Scale
from roguelike_engine.config.config_z_layer import DEFAULT_Z
from roguelike_game.ecs.systems.fsm.states.death_state import DeathState

try:
    import numpy as np
    from pygame import surfarray
    HAS_NUMPY = True
except ImportError:
    HAS_NUMPY = False

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
        # Cache de sprites escalados: clave = (entity_id, scale_factor)
        self._scaled_sprite_cache: dict[tuple[int, float], pygame.Surface] = {}
        # Rect reutilizable para culling y blit
        self._blit_rect = pygame.Rect(0, 0, 0, 0)

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
        comps      = world.components
        pos_map    = comps['Position']
        sprite_map = comps['Sprite']
        z_map      = comps.get('ZLayer', {})
        scale_map  = comps.get('Scale', {})

        # 3) Filtrar entidades visibles en el viewport
        visible_eids = [
            eid for eid, pos in pos_map.items()
            if eid in sprite_map
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
            if scale_factor != 1.0:
                key = (eid, scale_factor)
                cache = self._scaled_sprite_cache
                if key not in cache:
                    orig = sprite.image
                    w, h = orig.get_size()
                    # Usar rotozoom para mejor performance y suavidad
                    cache[key] = pygame.transform.rotozoom(orig, 0, scale_factor)
                image = cache[key]
            else:
                image = sprite.image

            # 5c) Convertir posición de mundo a pantalla
            dest = camapply((pos.x, pos.y))

            # 5d) Culling: saltar sprites fuera de la pantalla
            self._blit_rect.size = image.get_size()
            self._blit_rect.topleft = dest
            if not screen_rect.colliderect(self._blit_rect):
                continue

            # 5e) Acumular operación de blit
            blit_ops.append((image, dest))

        # 6) Ejecutar todos los blits en batch (más eficiente que múltiples blit individuales)
        if blit_ops:
            screen.blits(blit_ops)

        # Aplicar escala de grises si se ha marcado
        if world.components.get('GrayscaleComponent'):
            self.apply_grayscale(screen)

    def apply_grayscale(self, surface):
        """Convierte la superficie entera a escala de grises."""
        if HAS_NUMPY:
            arr = surfarray.array3d(surface)
            lum = (0.299 * arr[:, :, 0] + 0.587 * arr[:, :, 1] + 0.114 * arr[:, :, 2]).astype(np.uint8)
            gray3 = np.stack((lum, lum, lum), axis=-1)
            surfarray.blit_array(surface, gray3)
        else:
            pixels = pygame.PixelArray(surface)
            w, h = surface.get_size()
            for x in range(w):
                for y in range(h):
                    color = surface.unmap_rgb(pixels[x, y])
                    lum = int(0.299*color.r + 0.587*color.g + 0.114*color.b)
                    pixels[x, y] = surface.map_rgb((lum, lum, lum))
            del pixels