import pygame
from roguelike_engine.diagnostics.helpers import draw_debug_rect

class BuildingView:
    """
    Vista encargada de dibujar las partes superior e inferior de un BuildingModel
    según el zoom de la cámara. Mantiene caches locales para cada nivel de zoom.
    """

    def __init__(self, model, camera):
        """
        Recibe:
        • model: instancia de BuildingModel (con self.image ya cargada).
        • camera: objeto cámara, con propiedades .zoom y método scale().
        """
        self._model = model
        self._camera = camera

        # Caches por “zoom” redondeado (p. ej. 1.00, 1.25, 2.00)
        self._scaled_cache: dict[float, pygame.Surface] = {}
        self._render_part_cache: dict[float, tuple[pygame.Surface, pygame.Surface]] = {}
        # Referencia a la última surface fuente usada; si cambia, invalidamos caches
        self._last_image_ref: pygame.Surface | None = model.image

    def _get_scaled_image(self) -> pygame.Surface:
        """
        Devuelve la versión de self._model.image escalada al zoom actual.
        Mantiene un cache por zoom (~2 decimales).
        """
        # Si la surface fuente cambió (p. ej., cambio de estado visual), limpiar caches
        if self._model.image is not self._last_image_ref:
            self.clear_caches()
            self._last_image_ref = self._model.image
        zoom = round(self._camera.zoom, 2)
        if zoom not in self._scaled_cache:
            orig = self._model.image
            # Calculamos nuevo tamaño, usando camera.scale()
            new_size = self._camera.scale(orig.get_size())

            scaled = pygame.transform.scale(orig, new_size)
            self._scaled_cache[zoom] = scaled

            # Dividir en “parte superior” y “parte inferior”
            w, h = scaled.get_size()
            cut_pixel = int(h * self._model.split_ratio)
            top_surf = scaled.subsurface(pygame.Rect(0, 0, w, cut_pixel)).copy()
            bot_surf = scaled.subsurface(pygame.Rect(0, cut_pixel, w, h - cut_pixel)).copy()
            self._render_part_cache[zoom] = (top_surf, bot_surf)
        return self._scaled_cache[zoom]

    def render_part(self, screen: pygame.Surface, *, top: bool):
        """
        Dibuja en pantalla la parte indicada (parte “top” o “bottom”) del edificio:
        • Calcula el offset vertical para la parte inferior.
        • Opcionalmente, dibuja un rect de colisión si model.solid y no es la parte top.
        """
        zoom = round(self._camera.zoom, 2)
        if zoom not in self._render_part_cache:
            # Forzamos la generación de caches
            self._get_scaled_image()
        top_surf, bot_surf = self._render_part_cache[zoom]
        
        # Calculamos posición en pantalla, usando camera.apply para las coordenadas de world
        base_x, base_y = self._model.x, self._model.y
        screen_x, screen_y = self._camera.apply((base_x, base_y))

        if top:
            screen.blit(top_surf, (screen_x, screen_y))
        else:
            # La parte “bottom” debe dibujarse desplazada por la altura del top_render
            offset = top_surf.get_height()
            screen.blit(bot_surf, (screen_x, screen_y + offset))

            # Si el edificio es sólido, dibujamos (para debugging) el rect de colisión:
            if self._model.solid:
                rect = self._model.collision_rect
                draw_debug_rect(screen, self._camera, rect, color=(255,255,255), width=1)

    def clear_caches(self):
        """
        Limpia los caches de escalado (por si la imagen en el modelo cambió de tamaño).
        """
        self._scaled_cache.clear()
        self._render_part_cache.clear()