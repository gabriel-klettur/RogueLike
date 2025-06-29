# Path: src/roguelike_engine/buildings/building.py
import pygame
import types

from roguelike_engine.buildings.model.building_model import BuildingModel
from roguelike_engine.buildings.controller.building_controller import BuildingController

class Building:
    """
    Fachada ligera que expone una única clase Building para el resto del juego,
    delegando internamente en nuestro patrón MVC:
      • BuildingModel: almacena estado y lógica de datos.
      • BuildingView: se encarga de renderizar según la cámara.
      • BuildingController: orquesta modelo y vista, carga zona y collision_map,
        y expone métodos de render y actualización.
    """

    def __init__(
        self,
        rel_x: int,
        rel_y: int,
        image_path: str,
        camera=None,
        *,
        solid: bool = True,
        scale: tuple[int, int] | None = None,
        split_ratio: float = 0.5,
        z_bottom: int | None = None,
        z_top: int | None = None
    ):
        """
        Crea internamente el BuildingModel y el BuildingController (que a su vez
        generará la BuildingView ligada a la cámara). Luego se podrá llamar a:
          • assign_zone(zone_name)
          • load_collision_map(collision_data)
          • render(screen)
          • update_on_camera_change()
        """
        # 1) Instancia del modelo, que carga y ajusta la imagen
        self.model = BuildingModel(
            rel_x=rel_x,
            rel_y=rel_y,
            image_path=image_path,
            solid=solid,
            scale=scale,
            split_ratio=split_ratio,
            z_bottom=z_bottom,
            z_top=z_top
        )

        # 2) Instancia del controlador solo si se pasa cámara
        if camera is not None:
            self.controller = BuildingController(self.model, camera)
        else:
            self.controller = None

    def assign_zone(self, zone_name: str):
        """
        Asigna la zona al BuildingModel y actualiza sus coordenadas absolutas.
        Debe llamarse antes de renderizar si la zona no se había establecido aún.
        """
        if self.controller:
            self.controller.assign_zone(zone_name)

    def load_collision_map(self, collision_data: list[list[str]]):
        """
        Carga en el modelo la matriz de strings ('#' / '.') que define las
        celdas sólidas de este edificio. Al asignarla, se invalidan los caches
        de colisión internos.
        """
        if self.controller:
            self.controller.load_collision_map(collision_data)

    def render(self, screen):
        """
        Llama al controlador para que dibuje primero la parte inferior (bottom)
        y luego la superior (top) del edificio, usando la vista y el modelo.
        """
        if self.controller:
            self.controller.render(screen)

    def update_on_camera_change(self):
        """
        Debe invocarse cuando la cámara cambie (zoom u offset) para que la vista
        invalide sus caches de superficies escaladas.
        """
        if self.controller:
            self.controller.update_on_camera_change()

    @property
    def x(self) -> int:
        """Coordenada X absoluta en el mundo."""
        return self.model.x

    @x.setter
    def x(self, value: int):
        """Setter for absolute X coordinate."""
        self.model.x = value
        # invalidate collision tiles cache when moving building
        self.model._collision_tiles_cache = None
        self.model._collision_tile_objs = None
        if self.controller:
            self.controller.update_on_camera_change()

    @property
    def y(self) -> int:
        """Coordenada Y absoluta en el mundo."""
        return self.model.y

    @y.setter
    def y(self, value: int):
        """Setter for absolute Y coordinate."""
        self.model.y = value
        # invalidate collision tiles cache when moving building
        self.model._collision_tiles_cache = None
        self.model._collision_tile_objs = None
        if self.controller:
            self.controller.update_on_camera_change()

    @property
    def rel_x(self) -> int:
        """Coordenada X relativa en la zona."""
        return self.model.rel_x

    @rel_x.setter
    def rel_x(self, value: int):
        """Set relative X coordinate in zone."""
        self.model.rel_x = value

    @property
    def rel_y(self) -> int:
        """Coordenada Y relativa en la zona."""
        return self.model.rel_y

    @rel_y.setter
    def rel_y(self, value: int):
        """Set relative Y coordinate in zone."""
        self.model.rel_y = value

    @property
    def zone(self) -> str | None:
        """Zona asignada al edificio."""
        return self.model.zone

    @zone.setter
    def zone(self, value: str | None):
        """Setter para zona: actualiza modelo y controlador si existe."""
        self.model.zone = value
        if self.controller:
            self.controller.assign_zone(value)

    @property
    def original_scale(self) -> tuple[int, int] | None:
        """Escala original de la imagen cargada."""
        return self.model.original_scale

    @original_scale.setter
    def original_scale(self, value: tuple[int, int]):
        """Setter para restaurar escala original sin recargar."""
        self.model.original_scale = value

    @property
    def collision_rect(self):
        """Rectángulo de colisión completo (parte inferior) en coordenadas absolutas."""
        return self.model.collision_rect

    @property
    def collision_tiles(self) -> list[pygame.Rect]:
        """
        Lista de rectángulos (por tile) para cada celda sólida ('#') de este edificio.
        """
        return self.model.collision_tiles

    @property
    def collision_tile_objs(self) -> list[types.SimpleNamespace]:
        """
        Lista de objetos con atributos .solid y .rect para cada tile de colisión.
        """
        return self.model.collision_tile_objs

    @property
    def collision_map(self) -> list[list[str]]:
        """Raw collision map of the building."""
        return self.model.collision_map

    @collision_map.setter
    def collision_map(self, data: list[list[str]]):
        """Set collision map for the building and invalidate caches."""
        self.model.collision_map = data

    @property
    def z_bottom(self) -> int:
        """Capa Z inferior del edificio."""
        return self.model.z_bottom

    @z_bottom.setter
    def z_bottom(self, value: int):
        """Setter para z_bottom."""
        self.model.z_bottom = value
        if self.controller:
            self.controller.update_on_camera_change()

    @property
    def z_top(self) -> int:
        """Capa Z superior del edificio."""
        return self.model.z_top

    @z_top.setter
    def z_top(self, value: int):
        """Setter para z_top."""
        self.model.z_top = value
        if self.controller:
            self.controller.update_on_camera_change()

    @property
    def z(self) -> int:
        """Capa Z actual (alias de z_bottom)."""
        return self.model.z

    @z.setter
    def z(self, value: int):
        """Setter para capa Z: actualiza el modelo."""
        self.model.z = value

    @property
    def image(self) -> pygame.Surface:
        """Imagen del edificio (surface) para render y culling."""
        return self.model.image

    @property
    def split_ratio(self) -> float:
        """Proportion for splitting top/bottom of building image."""
        return self.model.split_ratio

    @split_ratio.setter
    def split_ratio(self, value: float):
        """Update split ratio and clear view caches."""
        # Clamp value between 0.0 and 1.0
        self.model.split_ratio = max(0.0, min(value, 1.0))
        if self.controller:
            self.controller.update_on_camera_change()

    @property
    def image_path(self) -> str:
        """Original image file path for this building."""
        return self.model.image_path

    @property
    def rect(self) -> pygame.Rect:
        """Bounding box of the full building image."""
        w, h = self.image.get_size()
        return pygame.Rect(self.x, self.y, w, h)

    @property
    def solid(self) -> bool:
        """Whether this building is solid."""
        return self.model.solid

    @solid.setter
    def solid(self, value: bool):
        """Set solidity of building."""
        self.model.solid = value

    @property
    def zone(self) -> str | None:
        """Zona asignada al edificio."""
        return self.model.zone

    @zone.setter
    def zone(self, value: str | None):
        """Setter para zona: actualiza modelo y controlador si existe."""
        self.model.zone = value
        if self.controller:
            self.controller.assign_zone(value)

    def get_parts(self) -> list[types.SimpleNamespace]:
        """
        Retorna partes renderizables (bottom y top) para render z-ordenado.
        Cada parte expone x, y, z, image y método render(screen, camera).
        """
        parts = []
        for top in (False, True):
            zval = self.z_bottom if not top else self.z_top
            def _render(screen, camera, top=top):
                # Lazy init controller/view if missing
                if self.controller is None:
                    self.controller = BuildingController(self.model, camera)
                self.controller.view.render_part(screen, top=top)
            part = types.SimpleNamespace(
                x=self.x,
                y=self.y,
                z=zval,
                image=self.image,
                render=_render
            )
            parts.append(part)
        return parts

    def resize(self, new_width: int, new_height: int):
        """
        Redimensiona la imagen del edificio a new_width×new_height, invalidando
        caches de renderizado internos. Se delega en el modelo.
        """
        self.model.resize(new_width, new_height)
        # Después de cambiar el tamaño en el modelo, hay que limpiar caches de vista:
        self.update_on_camera_change()

    def reset_to_original_size(self):
        """
        Restaura el tamaño original que tenía la imagen al cargarse. Se delega en el modelo.
        """
        self.model.reset_to_original_size()
        self.update_on_camera_change()

    def __repr__(self) -> str:
        return repr(self.model)