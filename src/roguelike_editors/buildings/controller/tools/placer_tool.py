
import pygame

from roguelike_editors.buildings.utils.zone_helpers import assign_zone_and_relatives
from roguelike_engine.config.config_tiles import TILE_SIZE

class PlacerTool:
    def __init__(self, state, editor_state, building_class, default_image, default_scale=(512, 512), default_solid=True):
        self.state = state
        self.editor = editor_state
        self.building_class = building_class
        self.default_image = default_image
        self.default_scale = default_scale
        self.default_solid = default_solid

    def place_building_at_mouse(self, buildings):
        mx, my = pygame.mouse.get_pos()
        world_x = mx / self.editor.camera.zoom + self.editor.camera.offset_x
        world_y = my / self.editor.camera.zoom + self.editor.camera.offset_y

        new_building = self.building_class(
            rel_x=int(world_x),
            rel_y=int(world_y),
            image_path=self.default_image,
            solid=self.default_solid,
            scale=None
        )
        # Asignar zona y coordenadas relativas
        assign_zone_and_relatives(new_building)
        # Initialize collision_map for new building
        w = new_building.image.get_width() // TILE_SIZE
        h = new_building.image.get_height() // TILE_SIZE
        new_building.collision_map = [["." for _ in range(w)] for _ in range(h)]

        buildings.append(new_building)
        print(f"➕ Edificio agregado en ({int(world_x)}, {int(world_y)}) [zona={new_building.zone}, rel=({new_building.rel_x},{new_building.rel_y})]")

    def place_building_at_path(self, buildings, world_x, world_y, image_path):
        """Nuevo: crea y coloca un building usando la ruta de asset indicada."""
        new_building = self.building_class(
            rel_x=int(world_x),
            rel_y=int(world_y),
            image_path=image_path,
            solid=self.default_solid,
            scale=None
        )
        # Asignar zona y coordenadas relativas
        assign_zone_and_relatives(new_building)
        # Initialize collision_map for new building
        w = new_building.image.get_width() // TILE_SIZE
        h = new_building.image.get_height() // TILE_SIZE
        new_building.collision_map = [["." for _ in range(w)] for _ in range(h)]

        buildings.append(new_building)
        print(f"➕ Edificio '{image_path}' colocado en ({int(world_x)}, {int(world_y)}) [zona={new_building.zone}, rel=({new_building.rel_x},{new_building.rel_y})]")