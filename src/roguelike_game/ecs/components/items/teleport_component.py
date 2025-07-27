class TeleportComponent:
    """
    Componente que almacena parámetros de teletransporte para un ítem.
    """
    def __init__(self, dest_map: str, dest_x: int, dest_y: int):
        self.dest_map = dest_map
        self.dest_x = dest_x
        self.dest_y = dest_y
