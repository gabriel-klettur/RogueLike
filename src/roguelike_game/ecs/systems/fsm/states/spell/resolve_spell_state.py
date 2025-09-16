from roguelike_game.ecs.systems.fsm.state import State
from roguelike_game.ecs.systems.fsm.states.spell.cooldown_state import CooldownState
from roguelike_game.ecs.systems.fsm.states.idle_state import IdleState


class ResolveSpellState(State):
    def enter(self, entity):
        # No se requiere acción al iniciar la fase de resolución
        pass

    def execute(self, entity, dt):
        world = entity.world
        # Para el jugador, ir directo a cooldown tras el release sin esperar colisión/expiración
        if entity.id == world.player_entity:
            # Si el hechizo actual es 'dash', no aplicar cooldown de hechizo; permitir chaining por cargas
            spell_key = self.fsm.context.get('spell') if hasattr(self, 'fsm') else None
            if spell_key == 'dash':
                entity.world.components['NPCState'][entity.id].fsm.change_state(IdleState(), entity)
                return
            from roguelike_game.ecs.systems.fsm.states.player.player_spell_cooldown_state import PlayerSpellCooldownState
            self.fsm.change_state(PlayerSpellCooldownState(), entity)
            return
        # Para NPCs, esperar a que la fireball desaparezca
        fid = self.fsm.context.get('fireball_id')
        if fid not in world.components.get('FireballComponent', {}):
            self.fsm.change_state(CooldownState(), entity)

    def exit(self, entity):
        pass