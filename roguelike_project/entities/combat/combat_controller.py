# roguelike_project/entities/combat/combat_controller.py

from roguelike_project.entities.combat.types.fireball import Fireball
from roguelike_project.entities.combat.visual_effects.laser_beam import LaserShot
import pygame

def shoot_fireball(player, angle):
    center_x = player.x + player.sprite_size[0] // 2
    center_y = player.y + player.sprite_size[1] // 2
    fireball = Fireball(center_x, center_y, angle, player.explosions)
    player.projectiles.append(fireball)

def shoot_laser(player, target_x, target_y, enemies):
    center_x = player.x + player.sprite_size[0] // 2
    center_y = player.y + player.sprite_size[1] // 2
    laser = LaserShot(center_x, center_y, target_x, target_y, enemies=enemies)
    player.lasers.append(laser)

    # Evita tener más de 3 láseres a la vez
    if len(player.lasers) > 3:
        player.lasers.pop(0)

def update_combat(player, state):
    solid_tiles = [tile for tile in state.tiles if tile.solid]
    enemies = state.enemies + list(state.remote_entities.values())

    # Actualizar proyectiles
    player.projectiles = [p for p in player.projectiles if p.alive]
    for p in player.projectiles:
        p.update(solid_tiles, enemies)

    # Actualizar explosiones
    player.explosions[:] = [e for e in player.explosions if not e.finished]
    for e in player.explosions:
        e.update()

    # 🔁 Fuego continuo de láser
    if player.shooting_laser:
        mouse_x, mouse_y = pygame.mouse.get_pos()
        world_mouse_x = mouse_x / state.camera.zoom + state.camera.offset_x
        world_mouse_y = mouse_y / state.camera.zoom + state.camera.offset_y
        shoot_laser(player, world_mouse_x, world_mouse_y, enemies)

    # Actualizar láseres activos
    for laser in player.lasers[:]:
        if not laser.update():
            player.lasers.remove(laser)
        
def render_combat_effects(player, screen, camera):
    for explosion in player.explosions:
        explosion.render(screen, camera)
    for laser in getattr(player, "lasers", []):
        laser.render(screen, camera)

