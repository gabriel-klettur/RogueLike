import pygame
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.ecs.systems.ai.chase_system import ChaseSystem

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
        # Obtener centros usando ChaseSystem
        center_x, center_y, origins = ChaseSystem.compute_centers(world)
        if origins is None or not origins:
            return
        scx = (center_x - camera.offset_x) * camera.zoom
        scy = (center_y - camera.offset_y) * camera.zoom
        # Dibujar centro del jugador
        pygame.draw.circle(screen, (0,255,0), (int(scx), int(scy)), 4)

        # Para cada NPC con ChaseTarget
        for eid, (origin_x, origin_y) in origins.items():
            pos = comps.get('Position', {}).get(eid)
            sprite = comps.get('Sprite', {}).get(eid)
            if not pos or not sprite:
                continue
            # Centro del NPC
            sox = (origin_x - camera.offset_x) * camera.zoom
            soy = (origin_y - camera.offset_y) * camera.zoom
            pygame.draw.circle(screen, (255,0,255), (int(sox), int(soy)), 4)
            # Línea hacia centro del jugador
            pygame.draw.line(screen, (0,0,255), (int(sox), int(soy)), (int(scx), int(scy)), 1)
            # Bounding box del NPC en amarillo
            w, h = sprite.image.get_size()
            # Ajustar escala de la entidad
            scale_cmp = comps.get('Scale', {}).get(eid)
            entity_scale = scale_cmp.scale if scale_cmp else 1.0
            scale_factor = entity_scale * camera.zoom
            sx = (pos.x - camera.offset_x) * camera.zoom
            sy = (pos.y - camera.offset_y) * camera.zoom
            sw = w * scale_factor
            sh = h * scale_factor
            pygame.draw.rect(screen, (255,255,0), pygame.Rect(sx, sy, sw, sh), 1)
            # Tile centrado en los pies del NPC
            foot_x = pos.x + (w * entity_scale) / 2
            foot_y = pos.y + (h * entity_scale)
            tile_x = (int(foot_x) // TILE_SIZE) * TILE_SIZE
            tile_y = (int(foot_y) // TILE_SIZE) * TILE_SIZE
            ts = TILE_SIZE * camera.zoom
            tsx = (tile_x - camera.offset_x) * camera.zoom
            tsy = (tile_y - camera.offset_y) * camera.zoom
            pygame.draw.rect(screen, (255,0,0), pygame.Rect(tsx, tsy, ts, ts), 1)
