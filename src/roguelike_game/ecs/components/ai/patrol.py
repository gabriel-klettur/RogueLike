from typing import Tuple, List, Dict, Optional, Any

class Patrol:
    """
    Component para el comportamiento de patrulla de NPC.
    origin: posición inicial en píxeles (x,y).
    waypoints: lista de posiciones en píxeles a recorrer en orden.
    speed: píxeles por actualización.
    current_index: índice del waypoint actual.
    sprites_by_direction: opcional mapeo de 'up','down','left','right' a lista de imágenes (pygame.Surface).
    sprite_per_point: opcional mapeo de índice de waypoint a imagen única.
    default_sprite: imagen por defecto si no hay otras.
    """
    def __init__(self,
                 origin: Tuple[float, float],
                 waypoints: Optional[List[Tuple[float, float]]] = None,
                 speed: float = 1.0,
                 sprites_by_direction: Optional[Dict[str, List[Any]]] = None,
                 sprite_per_point: Optional[Dict[int, Any]] = None):
        self.origin = origin
        self.speed = speed
        self.current_index = 0
        # generar waypoints por defecto si no se pasan
        if waypoints and len(waypoints) > 0:
            self.waypoints = waypoints
        else:
            ox, oy = origin
            offset = 200
            self.waypoints = [
                (ox + offset, oy),
                (ox, oy + offset),
                (ox - offset, oy),
                (ox, oy - offset),
            ]
        self.sprites_by_direction = sprites_by_direction or {}
        self.sprite_per_point = sprite_per_point or {}
        self.default_sprite = None
