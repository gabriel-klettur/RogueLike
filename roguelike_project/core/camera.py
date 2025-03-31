class Camera:
    def __init__(self, screen_width, screen_height):
        self.screen_width = screen_width
        self.screen_height = screen_height
        self.offset_x = 0
        self.offset_y = 0
        self.zoom = 1.0

    def update(self, target):
        self.offset_x = target.x - (self.screen_width / (2 * self.zoom))
        self.offset_y = target.y - (self.screen_height / (2 * self.zoom))

    def apply(self, pos):
        x, y = pos
        return int((x - self.offset_x) * self.zoom), int((y - self.offset_y) * self.zoom)

    def scale(self, size):
        """Escala (ancho, alto) según el zoom"""
        w, h = size
        return int(w * self.zoom), int(h * self.zoom)
