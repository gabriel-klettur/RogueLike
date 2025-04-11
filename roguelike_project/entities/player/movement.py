import pygame
import math

class PlayerMovement:
    def __init__(self, player):
        self.player = player
        self.speed = 10  # Velocidad normal de movimiento
        self.is_moving = False 
        self.last_move_dir = pygame.Vector2(0, 0)  # Dirección del último movimiento
        
        self.teleport_distance = 1000  # Distancia del teleport en píxeles
        self.teleport_cooldown = 0  # Tiempo en segundos para el cooldown del teleport
        self.last_teleport_time = 0  # Tiempo en que se realizó el último teleport

        self.dash_speed = 150  # Velocidad del dash (más rápido que la normal)
        self.dash_duration = 0.2  # Duración del dash en segundos
        self.dash_time_left = 0  # Tiempo restante del dash
        self.dash_direction = pygame.Vector2(0, 0)  # Dirección del dash
        self.is_dashing = False  # Bandera para saber si estamos en dash



    def teleport(self, mx, my):
        """ Teletransporta al jugador hacia la dirección del ratón """
        now = pygame.time.get_ticks() / 1000  # Obtén el tiempo actual en segundos
        if now - self.last_teleport_time < self.teleport_cooldown:
            return  # Si el cooldown no ha pasado, no hacemos nada

        # Calcular la dirección del ratón
        player_center_x = self.player.x + self.player.sprite_size[0] / 2
        player_center_y = self.player.y + self.player.sprite_size[1] / 2

        dx = mx - player_center_x
        dy = my - player_center_y
        distance = math.sqrt(dx ** 2 + dy ** 2)

        if distance != 0:
            dx /= distance
            dy /= distance        

        # Mover al jugador 300 píxeles en esa dirección
        self.player.x += dx * self.teleport_distance
        self.player.y += dy * self.teleport_distance

        self.last_teleport_time = now  # Actualiza el tiempo del último teleport

    def move(self, dx, dy, obstacles, solid_tiles):
        """ Movimiento normal del jugador """
        self.is_moving = False  # Reseteamos al inicio

        if dx != 0 or dy != 0:
            self.update_direction(dx, dy)

            # Normalizamos si hay movimiento diagonal
            if dx != 0 and dy != 0:
                norm = math.sqrt(dx ** 2 + dy ** 2)
                dx /= norm
                dy /= norm

            # Guardamos la dirección del movimiento normalizado
            self.last_move_dir = pygame.Vector2(dx, dy)
        else:
            self.last_move_dir = pygame.Vector2(0, 0)

        collided = False

        # Hitbox futura con movimiento aplicado
        future_hitbox = self.get_hitbox(
            self.player.x + dx * self.speed,
            self.player.y + dy * self.speed
        )

        # Colisión con tiles sólidos
        for tile in solid_tiles:
            if future_hitbox.colliderect(tile.rect):
                collided = True
                break

        # Colisión con obstáculos
        if not collided:
            for ob in obstacles:
                if future_hitbox.colliderect(ob.rect):
                    collided = True
                    break

        # Movimiento si no hay colisión
        if not collided:
            self.player.x += dx * self.speed
            self.player.y += dy * self.speed
            self.is_moving = True  # ✅ Movimiento permitido
            if self.player.hitbox is None:
                self.player.hitbox = self.get_hitbox(self.player.x, self.player.y)
            else:
                self.player.hitbox.topleft = (self.player.x + 20, self.player.y + 96)
        else:
            self.player.take_damage()

    def get_hitbox(self, x, y):
        return pygame.Rect(x + 20, y + 96, 56, 28)

    def update_direction(self, dx, dy):
        if dx == -1:
            self.player.direction = "left"
        elif dx == 1:
            self.player.direction = "right"
        elif dy == -1:
            self.player.direction = "up"
        elif dy == 1:
            self.player.direction = "down"
