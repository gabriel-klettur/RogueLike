from roguelike_game.ecs.fsm.state import State
from roguelike_game.ecs.fsm.states.player.player_spell_cast_state import PlayerSpellCastState
from roguelike_game.ecs.components.input_component import InputComponent

class PlayerSpellSelectState(State):
    def enter(self, entity):
        # Preparar selección de hechizo (espera input)
        pass

    def execute(self, entity, dt):
        inp = entity.world.components.get('InputComponent', {}).get(entity.id)
        if not inp:
            return
        fsm = entity.world.components['NPCState'][entity.id].fsm
        if inp.spell_lightball:
            fsm.context['spell'] = 'fireball'
            fsm.change_state(PlayerSpellCastState(), entity)
        elif inp.spell_slash:
            fsm.context['spell'] = 'slash'
            fsm.change_state(PlayerSpellCastState(), entity)

    def exit(self, entity):
        # Finaliza selección
        pass