
import pygame

class PlacerTool:
    def __init__(self, state, editor_state, building_class, default_image, default_scale=(512, 512), default_solid=True):
        self.state = state
        self.editor = editor_state
        self.building_class = building_class
        self.default_image = default_image
        self.default_scale = default_scale
        self.default_solid = default_solid

    def place_building_at_mouse(self):
        mx, my = pygame.mouse.get_pos()
        world_x = mx / self.state.camera.zoom + self.state.camera.offset_x
        world_y = my / self.state.camera.zoom + self.state.camera.offset_y

        new_building = self.building_class(
            x=world_x,
            y=world_y,
            image_path=self.default_image,
            solid=self.default_solid,
            scale=self.default_scale
        )

        self.state.buildings.append(new_building)
        print(f"➕ Edificio agregado en ({int(world_x)}, {int(world_y)})")
