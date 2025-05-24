import time
from roguelike_engine.utils.loader import load_image
from roguelike_game.systems.combat.explosions.fire import FireExplosion
import pygame

class DeathSystem:
    """
    Gestiona la muerte de NPCs: cambia sprite a cadáver, deshabilita movimiento,
    y elimina la entidad tras 60 segundos.
    """
    def __init__(self):
        # eid -> timestamp de muerte
        self.death_times = {}

    def update(self, world):
        now = time.time()
        # Recorre entidades con Health
        for eid, hp in list(world.components['Health'].items()):
            if hp.current_hp <= 0:
                # Obtener sprite y manejar muerte solo una vez
                sprite = world.components['Sprite'].get(eid)
                if not sprite:
                    continue
                if eid not in self.death_times:
                    # Usar imagen de muerte pre-cargada o cargar una vez
                    death_img = getattr(sprite, 'death_image', None)
                    if death_img is None:
                        death_path = getattr(sprite, 'death_image_path', None)
                        if death_path:
                            raw_img = load_image(death_path)
                            death_scale = getattr(sprite, 'death_scale', None)
                            if death_scale:
                                w, h = raw_img.get_size()
                                raw_img = pygame.transform.scale(raw_img, (int(w*death_scale), int(h*death_scale)))
                            death_img = raw_img
                        else:
                            death_img = sprite.image
                        sprite.death_image = death_img
                    sprite.image = death_img
                    # Deshabilitar sistemas de movimiento y animación
                    world.components['Patrol'].pop(eid, None)
                    world.components['Velocity'].pop(eid, None)
                    world.components['MultiCollider'].pop(eid, None)
                    world.components['Animator'].pop(eid, None)
                    # Registrar hora de muerte
                    self.death_times[eid] = now
                # Remover entidad tras 60s
                elif now - self.death_times[eid] >= 60:
                    world.remove_entity(eid)
                    del self.death_times[eid]
