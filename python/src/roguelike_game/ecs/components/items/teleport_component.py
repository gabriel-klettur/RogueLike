class TeleportComponent:
    """
    Componente que almacena parámetros de teletransporte para un ítem.
    """
    def __init__(self, dest_map: str | None = None, dest_x: int | None = None, dest_y: int | None = None,
                 dest_world: str | None = None, dest_zone: str | None = None):
        self.dest_map = dest_map
        self.dest_x = dest_x
        self.dest_y = dest_y
        self.dest_world = dest_world
        self.dest_zone = dest_zone
