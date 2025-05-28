import pygame
from roguelike_engine.config.config_tiles import TILE_SIZE

class ChaseDebugSystem:
    """
    Dibuja debug de ChaseSystem:
    - Centro del jugador en verde
    - Centro de cada NPC en magenta
    - Línea desde NPC al destino en azul
    """
    def __init__(self):
        self.debug = False
        self.last_pressed = False

    def update(self, world, screen, camera):
        # Toggle debug mode on F9
        keys = pygame.key.get_pressed()
        f9 = keys[pygame.K_F9]
        if f9 and not self.last_pressed:
            self.debug = not self.debug
        self.last_pressed = f9
        if not self.debug:
            return

        comps = world.components
        # Centro del jugador
        player_eid = getattr(world, 'player_entity', None)
        if player_eid is None:
            return
        player_pos = comps.get('Position', {}).get(player_eid)
        player_sprite = comps.get('Sprite', {}).get(player_eid)
        if not player_pos or not player_sprite:
            return
        center_x = player_pos.x + player_sprite.image.get_width() / 2
        center_y = player_pos.y + player_sprite.image.get_height() / 2
        scx = (center_x - camera.offset_x) * camera.zoom
        scy = (center_y - camera.offset_y) * camera.zoom
        # Dibujar centro del jugador
        pygame.draw.circle(screen, (0,255,0), (int(scx), int(scy)), 4)

        # Para cada NPC con ChaseTarget
        for eid, chase in list(comps.get('ChaseTarget', {}).items()):
            pos = comps.get('Position', {}).get(eid)
            sprite = comps.get('Sprite', {}).get(eid)
            if not pos or not sprite:
                continue
            # Centro del NPC
            origin_x = pos.x + sprite.image.get_width() / 2
            origin_y = pos.y + sprite.image.get_height() / 2
            sox = (origin_x - camera.offset_x) * camera.zoom
            soy = (origin_y - camera.offset_y) * camera.zoom
            pygame.draw.circle(screen, (255,0,255), (int(sox), int(soy)), 4)
            # Línea hacia centro del jugador
            pygame.draw.line(screen, (0,0,255), (int(sox), int(soy)), (int(scx), int(scy)), 1)
            # Bounding box del NPC en amarillo
            w, h = sprite.image.get_size()
            sx = (pos.x - camera.offset_x) * camera.zoom
            sy = (pos.y - camera.offset_y) * camera.zoom
            sw = w * camera.zoom
            sh = h * camera.zoom
            pygame.draw.rect(screen, (255,255,0), pygame.Rect(sx, sy, sw, sh), 1)
            # Tile donde está el NPC en rojo
            tile_x = (pos.x // TILE_SIZE) * TILE_SIZE
            tile_y = (pos.y // TILE_SIZE) * TILE_SIZE
            ts = TILE_SIZE * camera.zoom
            tsx = (tile_x - camera.offset_x) * camera.zoom
            tsy = (tile_y - camera.offset_y) * camera.zoom
            pygame.draw.rect(screen, (255,0,0), pygame.Rect(tsx, tsy, ts, ts), 1)
