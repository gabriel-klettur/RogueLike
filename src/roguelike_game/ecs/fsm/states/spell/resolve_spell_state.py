from roguelike_game.ecs.fsm.state import State
from roguelike_game.ecs.fsm.states.spell.cooldown_state import CooldownState
from roguelike_game.ecs.fsm.states.player.player_spell_cooldown_state import PlayerSpellCooldownState

class ResolveSpellState(State):
    def enter(self, entity):
        # No se requiere acción al iniciar la fase de resolución
        pass

    def execute(self, entity, dt):
        # Esperar hasta que la fireball haya colisionado o expirado
        fid = self.fsm.context.get('fireball_id')
        world = entity.world
        if fid not in world.components.get('FireballComponent', {}):
            # La fireball ya no existe: pasar a cooldown según tipo de entidad
            if entity.id == world.player_entity:
                self.fsm.change_state(PlayerSpellCooldownState(), entity)
            else:
                self.fsm.change_state(CooldownState(), entity)

    def exit(self, entity):
        pass