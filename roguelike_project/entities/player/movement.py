import pygame
import math
import time

class PlayerMovement:
    def __init__(self, player):
        self.player = player
        self.speed = 10
        self.is_moving = False 
        self.last_move_dir = pygame.Vector2(0, 0)
        
        self.teleport_distance = 1000
        self.teleport_cooldown = 0.5
        self.last_teleport_time = 0

        self.dash_speed = 2000
        self.dash_duration = 0.2
        self.dash_time_left = 0
        self.dash_direction = pygame.Vector2(0, 0)
        self.is_dashing = False
        self.last_dash_time = 0
        self.dash_cooldown = 2

    def teleport(self, mx, my):
        now = time.time()
        if now - self.last_teleport_time < self.teleport_cooldown:
            return

        self.last_teleport_time = now

        player_center_x = self.player.x + self.player.sprite_size[0] / 2
        player_center_y = self.player.y + self.player.sprite_size[1] / 2

        dx = mx - player_center_x
        dy = my - player_center_y
        distance = math.sqrt(dx ** 2 + dy ** 2)

        if distance != 0:
            dx /= distance
            dy /= distance        

        self.player.x += dx * self.teleport_distance
        self.player.y += dy * self.teleport_distance

    def start_dash_towards_mouse(self):
        now = time.time()
        if self.is_dashing:
            return
        if now - self.last_dash_time < self.dash_cooldown:
            return
        if self.player.stats.energy < 10:
            print("❌ Energía insuficiente para dash.")
            return

        mx, my = pygame.mouse.get_pos()
        px = self.player.x + self.player.sprite_size[0] / 2
        py = self.player.y + self.player.sprite_size[1] / 2

        dx = mx / self.player.state.camera.zoom + self.player.state.camera.offset_x - px
        dy = my / self.player.state.camera.zoom + self.player.state.camera.offset_y - py

        distance = math.hypot(dx, dy)
        if distance == 0:
            return

        direction = pygame.Vector2(dx / distance, dy / distance)
        self.dash_direction = direction
        self.dash_time_left = self.dash_duration
        self.is_dashing = True
        self.last_dash_time = now
        self.player.stats.energy -= 10

        self.player.state.effects.spawn_dash_trail(self.player, direction)

    def update_dash(self, solid_tiles, obstacles):
        if not self.is_dashing:
            return

        delta = self.player.state.clock.get_time() / 1000
        move_distance = self.dash_speed * delta
        dx = self.dash_direction.x * move_distance
        dy = self.dash_direction.y * move_distance

        future_hitbox = self.get_hitbox(
            self.player.x + dx,
            self.player.y + dy
        )

        collided = any(future_hitbox.colliderect(t.rect) for t in solid_tiles) or \
                   any(future_hitbox.colliderect(o.rect) for o in obstacles)

        if collided:
            self.is_dashing = False
            self.player.state.effects.spawn_dash_bounce(self.player.x, self.player.y)
            self.player.state.effects.stop_dash_trails()
            return

        self.player.x += dx
        self.player.y += dy
        self.dash_time_left -= delta
        if self.dash_time_left <= 0:
            self.is_dashing = False
            self.player.state.effects.stop_dash_trails()

    def move(self, dx, dy, obstacles, solid_tiles):
        self.is_moving = False

        if dx != 0 or dy != 0:
            self.update_direction(dx, dy)
            if dx != 0 and dy != 0:
                norm = math.sqrt(dx ** 2 + dy ** 2)
                dx /= norm
                dy /= norm
            self.last_move_dir = pygame.Vector2(dx, dy)
        else:
            self.last_move_dir = pygame.Vector2(0, 0)

        collided = False
        future_hitbox = self.get_hitbox(
            self.player.x + dx * self.speed,
            self.player.y + dy * self.speed
        )

        for tile in solid_tiles:
            if future_hitbox.colliderect(tile.rect):
                collided = True
                break

        if not collided:
            for ob in obstacles:
                if future_hitbox.colliderect(ob.rect):
                    collided = True
                    break

        if not collided:
            self.player.x += dx * self.speed
            self.player.y += dy * self.speed
            self.is_moving = True
            if self.player.hitbox is None:
                self.player.hitbox = self.get_hitbox(self.player.x, self.player.y)
            else:
                self.player.hitbox.topleft = (self.player.x + 20, self.player.y + 96)
        else:
            self.player.take_damage()
            print("🧱 Recibido daño por colision de movimiento")

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
