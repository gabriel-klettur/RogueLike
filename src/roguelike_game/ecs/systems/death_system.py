import time
from roguelike_engine.utils.loader import load_image
from roguelike_game.systems.combat.explosions.fire import FireExplosion

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
                # Primera vez que detectamos muerte
                if eid not in self.death_times:
                    # Cambiar sprite a cadáver
                    sprite = world.components['Sprite'].get(eid)
                    if sprite:
                        sprite.image = load_image(
                            "assets/npc/monsters/barbol/barbol_female_deth.png"
                        )
                    # Deshabilitar movimiento y colisiones
                    world.components['Patrol'].pop(eid, None)
                    world.components['Velocity'].pop(eid, None)
                    world.components['MultiCollider'].pop(eid, None)
                    # Registrar hora de muerte
                    self.death_times[eid] = now
                # Si han pasado >=60s, remover entidad
                elif now - self.death_times[eid] >= 60:
                    world.remove_entity(eid)
                    # Podríamos disparar un evento "corpse_removed" aquí
                    del self.death_times[eid]
