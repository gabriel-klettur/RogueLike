"""
Calibrador de edificios: ajusta rects y posiciones.
"""
class BuildingsCalibrator:
    """
    Actualiza el rect de colisión/render de cada edificio,
    usando las propiedades x,y derivadas de rel_x/rel_y y zone.
    """
    def recalibrate(self, buildings):
        for b in buildings:
            if getattr(b, "zone", None) is not None and getattr(b, "rel_x", None) is not None:
                abs_x, abs_y = b.x, b.y
                if hasattr(b, "rect"):
                    b.rect.topleft = (abs_x, abs_y)
