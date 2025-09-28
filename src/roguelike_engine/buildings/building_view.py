import pygame
import time

from roguelike_engine.diagnostics.helpers import draw_debug_rect
from roguelike_engine.buildings.services.types import CameraProtocol
from roguelike_engine.buildings.building_model import BuildingModel

class BuildingView:
    """
    Vista encargada de dibujar las partes superior e inferior de un BuildingModel
    según el zoom de la cámara. Mantiene caches locales para cada nivel de zoom.
    """

    def __init__(self, model: BuildingModel, camera: CameraProtocol) -> None:
        """
        Recibe:
        • model: instancia de BuildingModel (con self.image ya cargada).
        • camera: objeto que implementa CameraProtocol (zoom, scale, apply).
        """
        self._model: BuildingModel = model
        self._camera: CameraProtocol = camera

        # Caches por “zoom” redondeado (p. ej. 1.00, 1.25, 2.00)
        # Nota: mantenemos el caché de imagen escalada por zoom; las partes top/bottom
        # dependen además del split_ratio, por lo que se indexan por (zoom, split_key).
        self._scaled_cache: dict[float, pygame.Surface] = {}
        self._render_part_cache: dict[tuple[float, float], tuple[pygame.Surface, pygame.Surface]] = {}

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
        # Asegurar caché de imagen escalada por zoom
        if zoom not in self._scaled_cache:
            orig = self._model.image
            # Calculamos nuevo tamaño, usando camera.scale()
            new_size = self._camera.scale(orig.get_size())
            scaled = pygame.transform.scale(orig, new_size)
            self._scaled_cache[zoom] = scaled

        # Asegurar caché de partes por (zoom, split_ratio)
        split_key = round(float(self._model.split_ratio), 4)
        key = (zoom, split_key)
        if key not in self._render_part_cache:
            scaled = self._scaled_cache[zoom]
            w, h = scaled.get_size()
            cut_pixel = int(h * self._model.split_ratio)
            top_surf = scaled.subsurface(pygame.Rect(0, 0, w, cut_pixel)).copy()
            bot_surf = scaled.subsurface(pygame.Rect(0, cut_pixel, w, h - cut_pixel)).copy()
            self._render_part_cache[key] = (top_surf, bot_surf)
        return self._scaled_cache[zoom]

    def render_part(self, screen: pygame.Surface, *, top: bool) -> None:
        """
        Dibuja en pantalla la parte indicada (parte “top” o “bottom”) del edificio:
        • Calcula el offset vertical para la parte inferior.
        • Opcionalmente, dibuja un rect de colisión si model.solid y no es la parte top.
        """
        zoom = round(self._camera.zoom, 2)
        split_key = round(float(self._model.split_ratio), 4)
        key = (zoom, split_key)
        if key not in self._render_part_cache:
            # Forzamos la generación de caches (asegura scaled y partes)
            self._get_scaled_image()
        top_surf, bot_surf = self._render_part_cache[key]

        # Calculamos posición en pantalla, usando camera.apply para las coordenadas de world
        base_x, base_y = self._model.x, self._model.y
        screen_x, screen_y = self._camera.apply((base_x, base_y))

        # Check flash state (damage tint)
        # We avoid mutating cached surfaces by working on copies when flashing
        now = time.time()
        flashing = False
        color = (255, 255, 255)
        blink_ok = True
        try:
            flashing = now < getattr(self._model, '_flash_until_ts', 0.0)
            color = tuple(getattr(self._model, '_flash_color', (255, 255, 255)))
            bi = float(getattr(self._model, '_flash_blink_interval', 0.05) or 0.0)
            if flashing and bi > 0.0:
                # Global time-based blink; simple and stable
                blink_ok = (int(now / bi) % 2 == 0)
        except Exception:
            flashing = False
            blink_ok = True

        if top:
            surf_to_blit = top_surf
            if flashing and blink_ok:
                try:
                    tmp = top_surf.copy()
                    tmp.fill(color, special_flags=pygame.BLEND_RGB_ADD)
                    surf_to_blit = tmp
                except Exception:
                    surf_to_blit = top_surf
            screen.blit(surf_to_blit, (screen_x, screen_y))
        else:
            # La parte “bottom” debe dibujarse desplazada por la altura del top_render
            offset = top_surf.get_height()
            surf_to_blit = bot_surf
            if flashing and blink_ok:
                try:
                    tmp = bot_surf.copy()
                    tmp.fill(color, special_flags=pygame.BLEND_RGB_ADD)
                    surf_to_blit = tmp
                except Exception:
                    surf_to_blit = bot_surf
            screen.blit(surf_to_blit, (screen_x, screen_y + offset))

            # Si el edificio es sólido, dibujamos (para debugging) el rect de colisión:
            if self._model.solid:
                rect = self._model.collision_rect
                draw_debug_rect(screen, self._camera, rect, color=(255,255,255), width=1)

    def clear_caches(self) -> None:
        """
        Limpia los caches de escalado (por si la imagen en el modelo cambió de tamaño).
        """
        self._scaled_cache.clear()
        self._render_part_cache.clear()