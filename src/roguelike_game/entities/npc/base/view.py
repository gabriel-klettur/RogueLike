# Path: src/roguelike_game/entities/npc/base/view.py

import pygame
from roguelike_game.entities.npc.interfaces import IView

class NPCView(IView):
    """
    Vista base para NPCs: recibe un dict de rutas de sprites y un tamaño
    genérico, y provee render de la barra de vida.
    """
    def __init__(self, model, sprite_paths: dict[str, str], sprite_size: tuple[int,int]):
        self.model = model
        self.sprite_paths = sprite_paths    # ej. {"up": "ruta/up.png", ...}
        self.sprite_size  = sprite_size     # ej. (256,256)
        self.sprites = {}                   # aquí cargarán las subclases
        self.mask = None

    def _render_health_bar(self, screen, camera, offset=(18, -20)):
        m = self.model
        x, y = camera.apply((m.x + offset[0], m.y + offset[1]))
        bar_w = int(self.sprite_size[0] * camera.zoom * 0.9)
        bar_h = int(20 * camera.zoom)
        # fondo
        pygame.draw.rect(screen, (40,40,40), (x, y, bar_w, bar_h))
        # fill
        fill = int(bar_w * (m.health / m.max_health))
        pygame.draw.rect(screen, (0,255,0), (x, y, fill, bar_h))
        # borde
        pygame.draw.rect(screen, (0,0,0), (x, y, bar_w, bar_h), 1)

    def render(self, screen, camera):
        """
        Método mínimo: renderiza la barra de vida. Las subclases deben:
         - cargar los sprites en self.sprites
         - seleccionar y blit el frame correcto antes o después de llamar aquí
        """
        if not self.model.alive:
            return
        self._render_health_bar(screen, camera)
