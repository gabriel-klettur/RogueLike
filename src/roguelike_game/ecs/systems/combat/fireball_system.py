import pygame

class FireballSystem:
    """
    Sistema que actualiza fireballs: movimiento, edad, colisiones con NPC y tiles.
    """
    def update(self, world, camera, perf_log=None):
        # Actualizar cada fireball
        for eid in list(world.components.get('FireballComponent', {})):
            comp = world.components['FireballComponent'][eid]
            pos = world.components['Position'][eid]
            vel = world.components['Velocity'][eid]
            # Movimiento
            pos.x += vel.vx
            pos.y += vel.vy
            comp.age += 1
            # Expirar por lifespan
            if comp.age >= comp.lifespan:
                world.remove_entity(eid)
                continue
            # Colisión con NPCs
            for target in world.get_entities_with('Position', 'MultiCollider', 'Health'):
                # Saltar self, caster y cadáveres con DeathTimer
                if target == eid or target == comp.caster:
                    continue
                if target in world.components.get('DeathTimer', {}):
                    continue
                multi = world.components['MultiCollider'][target]
                body = multi.colliders.get('body')
                if body:
                    tpos = world.components['Position'][target]
                    # Usar tamaño de mask si existe, sino width/height
                    if hasattr(body, 'mask'):
                        w, h = body.mask.get_size()
                    else:
                        w, h = body.width, body.height
                    rect = pygame.Rect(tpos.x + body.offset_x,
                                       tpos.y + body.offset_y,
                                       w, h)
                    if rect.collidepoint(pos.x, pos.y):
                        hp = world.components['Health'][target]
                        hp.current_hp = max(0, hp.current_hp - comp.damage)
                        world.remove_entity(eid)
                        break
            # Colisión con tiles sólidos
            point = pygame.Rect(pos.x, pos.y, 1, 1)
            nearby = world.get_solid_tiles_for_rect(point)
            if nearby and point.collidelist(nearby) != -1:
                world.remove_entity(eid)
                continue
