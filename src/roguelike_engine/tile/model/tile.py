import pygame
from roguelike_engine.config.config_tiles import TILE_SIZE

class Tile:
    __slots__ = ('x','y','tile_type','sprite','solid','rect','scaled_cache','overlay_code','zone')
    """
    Modelo puro de un tile:
      - x,y: posición en píxeles
      - tile_type: carácter que define el tipo de tile ('.', '#', etc.)
      - sprite: superficie de Pygame para dibujar
      - solid: si colisiona o no (p.ej. muro = True, suelo = False)
      - rect: pygame.Rect para detectar colisiones/eventos
      - scaled_cache: cache de sprites escalados por zoom
    """
    def __init__(
        self,
        x: int,
        y: int,
        tile_type: str,
        sprite: pygame.Surface,
        overlay_code: str | None = None
    ):
        self.x = x
        self.y = y
        self.tile_type = tile_type
        self.sprite = sprite

        # solid se infería antes con (tile_type == "#")
        self.solid = (tile_type == "#")

        # Rect en coordenadas mundo (útil para click / colisión)
        self.rect = pygame.Rect(x, y, TILE_SIZE, TILE_SIZE)

        # Cache de versiones escaladas del sprite, key = zoom
        self.scaled_cache: dict[float, pygame.Surface] = {}
        
        # Overlay code
        self.overlay_code = overlay_code
        # Zone de mapa (inicializada luego)
        self.zone: str | None = None