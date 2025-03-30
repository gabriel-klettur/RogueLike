# roguelike_project/core/camera.py

class Camera:
    def __init__(self, screen_width, screen_height):
        self.screen_width = screen_width
        self.screen_height = screen_height
        self.offset_x = 0
        self.offset_y = 0

    def update(self, target):
        self.offset_x = target.x - self.screen_width // 2
        self.offset_y = target.y - self.screen_height // 2

    def apply(self, pos):
        return pos[0] - self.offset_x, pos[1] - self.offset_y
