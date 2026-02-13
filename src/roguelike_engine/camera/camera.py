class Camera:
    def __init__(self, screen_width, screen_height):
        self.screen_width = screen_width
        self.screen_height = screen_height
        self.offset_x = 0
        self.offset_y = 0
        self.zoom = 1.0

    def resize(self, new_width, new_height):
        """Update screen dimensions after a window resize."""
        self.screen_width = new_width
        self.screen_height = new_height

    def update(self, target):
        self.offset_x = target.x - (self.screen_width / (2 * self.zoom))
        self.offset_y = target.y - (self.screen_height / (2 * self.zoom))
        # Align offsets to pixel grid (so that (world - offset) * zoom lands on integers)
        self._snap_offsets_to_pixel_grid()

    def apply(self, pos):
        x, y = pos
        z = self.zoom or 1.0
        # Use aligned offsets for pixel-perfect mapping
        ox = round(self.offset_x * z) / z
        oy = round(self.offset_y * z) / z
        return int(round((x - ox) * z)), int(round((y - oy) * z))

    def scale(self, size):
        """Escala (ancho, alto) según el zoom"""
        w, h = size
        return int(round(w * self.zoom)), int(round(h * self.zoom))

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

    def _snap_offsets_to_pixel_grid(self):
        """Snap offsets so that (offset * zoom) is an integer.
        This avoids subpixel sampling seams when blitting chunk/tile surfaces.
        """
        z = self.zoom or 1.0
        step = 1.0 / z
        try:
            self.offset_x = round(self.offset_x / step) * step
            self.offset_y = round(self.offset_y / step) * step
        except Exception:
            # Be resilient if offsets are not numbers
            pass