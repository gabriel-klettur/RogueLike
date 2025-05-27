import pygame

class PlayerDebugRenderSystem:
    """
    Dibuja bounding box y centro del sprite del jugador cuando se presiona F9.
    """
    def __init__(self):
        # Estado de toggling de debug
        self.debug = False
        self.last_pressed = False

    def update(self, world, screen, camera):
        # Toggle debug mode on F9 press
        keys = pygame.key.get_pressed()
        f9 = keys[pygame.K_F9]
        if f9 and not self.last_pressed:
            self.debug = not self.debug
        self.last_pressed = f9
        if not self.debug:
            return

        # Obtener entidad ECS del jugador
        player_eid = getattr(world, 'player_entity', None)
        if player_eid is None:
            return

        comps = world.components
        sprite = comps.get('Sprite', {}).get(player_eid)
        pos = comps.get('Position', {}).get(player_eid)
        if not sprite or not pos:
            return

        # Obtener tamaño del sprite
        w, h = sprite.image.get_size()
        # Calcular rect en pantalla
        sx = (pos.x - camera.offset_x) * camera.zoom
        sy = (pos.y - camera.offset_y) * camera.zoom
        sw = w * camera.zoom
        sh = h * camera.zoom

        # Dibujar rectángulo rojo alrededor del sprite
        rect = pygame.Rect(sx, sy, sw, sh)
        pygame.draw.rect(screen, (255, 0, 0), rect, 1)

        # Dibujar centro en verde
        cx = (pos.x + w / 2 - camera.offset_x) * camera.zoom
        cy = (pos.y + h / 2 - camera.offset_y) * camera.zoom
        pygame.draw.circle(screen, (0, 255, 0), (int(cx), int(cy)), 3)
        # Debug: dibujar destino de NPCs (chase target) en azul
        pygame.draw.circle(screen, (0, 0, 255), (int(cx), int(cy)), 6, 1)
