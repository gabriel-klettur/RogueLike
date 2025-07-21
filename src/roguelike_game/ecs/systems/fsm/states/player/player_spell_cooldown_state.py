from roguelike_game.ecs.systems.fsm.state import State
from roguelike_game.ecs.systems.fsm.states.idle_state import IdleState
from roguelike_game.config.spells_config import SPELLS
import time
import pygame
from roguelike_game.ecs.components.magic_spell_bar_component import MagicSpellBarComponent


class PlayerSpellCooldownState(State):
    """Wrapper para cooldown de hechizo del jugador."""
    def enter(self, entity):
        # Iniciar temporizador de cooldown
        self.fsm.context['cooldown_start'] = time.time()
        # Crear barra de cooldown
        start = self.fsm.context['cooldown_start']
        spell_key = self.fsm.context.get('spell')
        base = SPELLS.get(spell_key, {}).get('cooldown_duration', 0)
        punish = self.fsm.context.get('automatic_cast_punish', 1.0) if self.fsm.context.get('automatic', False) else 1.0
        duration = base * punish
        world = entity.world
        world.components.setdefault('MagicSpellBarComponent', {})[entity.id] = MagicSpellBarComponent(duration=duration, start_time=start, active=True, state='cooldown')

    def execute(self, entity, dt):
        # Duración de cooldown con penalización si automatic
        spell_key = self.fsm.context.get('spell')
        base = SPELLS.get(spell_key, {}).get('cooldown_duration', 0)
        punish = self.fsm.context.get('automatic_cast_punish', 1.0) if self.fsm.context.get('automatic', False) else 1.0
        duration = base * punish
        elapsed = time.time() - self.fsm.context.get('cooldown_start', 0)
        if elapsed >= duration:
            # Debug del cooldown
            spell_key = self.fsm.context.get('spell', '')
            print(f"[FSM DEBUG] Eid={entity.id} state PlayerSpellCooldownState -> IdleState (cooldown {elapsed:.2f}s spell={spell_key})")
            # Recast automático: si automatic y botón sigue presionado, reiniciar la sub-FSM a PrepareSpellState
            if self.fsm.context.get('automatic', False) and pygame.mouse.get_pressed()[0]:                
                from roguelike_game.ecs.systems.fsm.states.spell.prepare_spell_state import PrepareSpellState
                self.fsm.change_state(PrepareSpellState(), entity)
                return
            # No recast: salir a estado global IdleState
            entity.world.components['NPCState'][entity.id].fsm.change_state(IdleState(), entity)

    def exit(self, entity):
        # No requiere limpieza adicional
        pass