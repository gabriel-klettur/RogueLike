import pygame

class PlayerMovement:
    def __init__(self, player):
        self.player = player
        self.speed = 4

    def move(self, dx, dy, collision_mask, obstacles):
        collided = False

        if dx != 0 or dy != 0:
            self.update_direction(dx, dy)
            self.player.sprite = self.player.sprites[self.player.direction]

        if dx != 0:
            new_x = self.player.x + dx * self.speed
            px = new_x + self.player.sprite_size[0] // 2
            py = self.player.y + self.player.sprite_size[1] - 10
            if 0 <= px < collision_mask.get_width() and 0 <= py < collision_mask.get_height():
                color = collision_mask.get_at((px, py))
                if color == pygame.Color(255, 255, 255):
                    future_hitbox = self.get_hitbox(new_x, self.player.y)
                    for ob in obstacles:
                        if future_hitbox.colliderect(ob.rect):
                            collided = True
                            break
                    if not collided:
                        self.player.x = new_x
                        self.player.hitbox.topleft = (self.player.x + 20, self.player.y + 96)

        if dy != 0:
            new_y = self.player.y + dy * self.speed
            px = self.player.x + self.player.sprite_size[0] // 2
            py = new_y + self.player.sprite_size[1] - 10
            if 0 <= px < collision_mask.get_width() and 0 <= py < collision_mask.get_height():
                color = collision_mask.get_at((px, py))
                if color == pygame.Color(255, 255, 255):
                    future_hitbox = self.get_hitbox(self.player.x, new_y)
                    for ob in obstacles:
                        if future_hitbox.colliderect(ob.rect):
                            collided = True
                            break
                    if not collided:
                        self.player.y = new_y
                        self.player.hitbox.topleft = (self.player.x + 20, self.player.y + 96)

        if collided:
            self.player.take_damage()

    def get_hitbox(self, x, y):
        return pygame.Rect(x + 20, y + 96, 56, 28)

    def update_direction(self, dx, dy):
        if dx == -1 and dy == -1:
            self.player.direction = "up_left"
        elif dx == 1 and dy == -1:
            self.player.direction = "up_right"
        elif dx == -1 and dy == 1:
            self.player.direction = "down_left"
        elif dx == 1 and dy == 1:
            self.player.direction = "down_right"
        elif dx == -1:
            self.player.direction = "left"
        elif dx == 1:
            self.player.direction = "right"
        elif dy == -1:
            self.player.direction = "up"
        elif dy == 1:
            self.player.direction = "down"
