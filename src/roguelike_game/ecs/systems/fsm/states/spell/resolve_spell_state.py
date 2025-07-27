from roguelike_game.ecs.systems.fsm.state import State
from roguelike_game.ecs.systems.fsm.states.spell.cooldown_state import CooldownState


class ResolveSpellState(State):
    def enter(self, entity):
        # No se requiere acción al iniciar la fase de resolución
        pass

    def execute(self, entity, dt):
        world = entity.world
        # Para el jugador, ir directo a cooldown tras el release sin esperar colisión/expiración
        if entity.id == world.player_entity:
            from roguelike_game.ecs.systems.fsm.states.player.player_spell_cooldown_state import PlayerSpellCooldownState
            self.fsm.change_state(PlayerSpellCooldownState(), entity)
            return
        # Para NPCs, esperar a que la fireball desaparezca
        fid = self.fsm.context.get('fireball_id')
        if fid not in world.components.get('FireballComponent', {}):
            self.fsm.change_state(CooldownState(), entity)

    def exit(self, entity):
        pass