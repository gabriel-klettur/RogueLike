import pygame
from roguelike_engine.utils.benchmark import benchmark

class PlayerFacingSystem:
    """
    Sistema que actualiza Animator.current_state para el jugador
    basándose en la posición del ratón y su velocidad (idle/walk).
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, "4.2.2.PlayerFacingSystem.update")
    def update(self, world, camera=None):
        comps = world.components
        pos_map = comps.get('Position', {})
        vel_map = comps.get('Velocity', {})
        anim_map = comps.get('Animator', {})
        players = comps.get('PlayerTagComponent', {})
        
        for eid in players:
            animator = anim_map.get(eid)
            pos = pos_map.get(eid)
            if not animator or not pos or camera is None:
                continue
            vel = vel_map.get(eid)
            vx = vel.vx if vel else 0
            vy = vel.vy if vel else 0
            mx, my = pygame.mouse.get_pos()
            world_x = mx / camera.zoom + camera.offset_x
            world_y = my / camera.zoom + camera.offset_y
            dx = world_x - pos.x
            dy = world_y - pos.y
            # elegir dirección cardinal basada en ratón
            if abs(dx) > abs(dy):
                direction = 'right' if dx > 0 else 'left'
            else:
                direction = 'down' if dy > 0 else 'up'
            # determinar estado idle o walk
            state = f"{direction}_idle" if vx == 0 and vy == 0 else f"{direction}_walk"
            # aplicar estado si existe la animación
            if state in animator.animations and animator.current_state != state:
                animator.current_state = state