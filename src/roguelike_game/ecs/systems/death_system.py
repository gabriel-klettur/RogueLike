import time
from roguelike_engine.utils.loader import load_image
from roguelike_game.ecs.components.death_timer import DeathTimer
from roguelike_game.systems.combat.explosions.fire import FireExplosion
import pygame

class DeathSystem:
    """
    Gestiona la muerte de NPCs: cambia sprite a cadáver, deshabilita movimiento,
    y elimina la entidad tras 60 segundos.
    """
    def __init__(self):
        # Ahora usamos componente DeathTimer para almacenar el tiempo de muerte
        pass

    def update(self, world):
        now = time.time()
        dt_store = world.components['DeathTimer']
        # Recorrer entidades con Health=0 y manejar muerte via componente
        for eid, hp in list(world.components['Health'].items()):
            if hp.current_hp > 0:
                continue
            sprite = world.components['Sprite'].get(eid)
            if not sprite:
                continue
            # Primera muerte: asignar sprite de muerte y registrar componente
            if eid not in dt_store:
                # Cambiar imagen a la pre-cargada o cargar una vez
                death_img = getattr(sprite, 'death_image', None)
                if death_img is None:
                    death_path = getattr(sprite, 'death_image_path', None)
                    if death_path:
                        raw = load_image(death_path)
                        scale_v = getattr(sprite, 'death_scale', None)
                        if scale_v:
                            w_, h_ = raw.get_size()
                            raw = pygame.transform.scale(raw, (int(w_*scale_v), int(h_*scale_v)))
                        death_img = raw
                    else:
                        death_img = sprite.image
                    sprite.death_image = death_img
                sprite.image = death_img
                # Deshabilitar movimiento y animación
                world.components['Patrol'].pop(eid, None)
                world.components['Velocity'].pop(eid, None)
                world.components['MultiCollider'].pop(eid, None)
                world.components['Animator'].pop(eid, None)
                # Registrar componente DeathTimer
                dt_store[eid] = DeathTimer(start_time=now, duration=60.0)
            else:
                dt = dt_store[eid]
                # Expirar y eliminar
                if now - dt.start_time >= dt.duration:
                    # Eliminar entidad (world.remove_entity ya limpia el componente DeathTimer)
                    world.remove_entity(eid)
                    # No eliminar dt_store manualmente para evitar KeyError
                    continue
