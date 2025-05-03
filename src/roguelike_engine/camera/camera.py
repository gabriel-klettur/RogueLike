# Path: src/roguelike_engine/camera/camera.py

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

    def is_in_view(self, x, y, size):
        """
        Verifica si un objeto en (x, y) con tamaño (w, h) está dentro
        del área visible. Si size es None, asumimos que debe dibujarse.
        """
        if size is None:
            return True

        screen_x, screen_y = self.apply((x, y))
        w, h = self.scale(size)
        return -w < screen_x < self.screen_width and -h < screen_y < self.screen_height
