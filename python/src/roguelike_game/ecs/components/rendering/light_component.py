class LightComponent:
    def __init__(
        self,
        radius: int = 160,
        color: tuple[int, int, int] = (255, 200, 140),
        intensity: float = 1.0,
        falloff: float = 2.0,
        enabled: bool = True,
        flicker_amp: float = 0.0,
        flicker_speed: float = 2.3,
        center_scale: float = 1.0,
    ):
        self.radius = int(radius)
        self.color = (int(color[0]), int(color[1]), int(color[2]))
        self.intensity = float(intensity)
        self.falloff = float(falloff)
        self.enabled = bool(enabled)
        self.flicker_amp = float(flicker_amp)
        self.flicker_speed = float(flicker_speed)
        self.center_scale = float(center_scale)
