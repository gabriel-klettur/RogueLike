"""
Module: melee_combat_system.py
Contains the MeleeCombatSystem which resolves melee attack intents
and applies damage to targets based on attacker stats, weapon bonuses,
and target defense.
"""

from roguelike_game.ecs.components.combat.combat_stats import CombatStats
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.fsm.states.chase_state import ChaseState
from roguelike_game.ecs.fsm.states.damage_state import DamageState
from roguelike_game.ecs.systems.fsm.fsm_system import _EntityProxy

class MeleeCombatSystem:
    """
    Sistema que procesa eventos de WantsToMelee y aplica daño.
    
    Para cada intención de ataque:
      1. Recupera estadísticas de atacante y objetivo.
      2. Calcula daño = max(0, ataque + bonus_arma - defensa).
      3. Reduce los puntos de vida del objetivo.
      4. Elimina el componente WantsToMelee para limpiar el evento.      
    """

    def __init__(self, perf_log):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, "4.2.2.MeleeCombatSystem.update")
    def update(self, world, camera=None):
        """
        Recorre todos los eventos WantsToMelee registrados en el mundo,
        aplica el daño calculado y luego purga cada evento para evitar
        procesarlo de nuevo en el siguiente ciclo.
        """
        # Iterar sobre una copia de los items para poder eliminar sobre la marcha
        for eid, intent in list(world.components['WantsToMelee'].items()):
            # Obtener estadísticas de atacante y defensor
            attacker_stats: CombatStats = world.components['CombatStats'][intent.attacker]
            defender_stats: CombatStats = world.components['CombatStats'][intent.target]
            
            # Bonus de arma si existe
            weapon_comp = world.components['MeleeWeapon'].get(intent.attacker)
            weapon_bonus = weapon_comp.damage if weapon_comp else 0
            
            # Cálculo de daño neto (no negativo)
            raw_damage = attacker_stats.power + weapon_bonus - defender_stats.defense
            damage = max(0, raw_damage)
            
            # Aplicar daño al objetivo
            defender_stats.current_hp -= damage
            
            # NPC recibe daño de jugador -> mostrar sprite y luego chase
            if intent.attacker in world.components.get('PlayerTagComponent', {}):
                target_eid = intent.target
                # determinar dirección de daño
                attacker_pos = world.components['Position'][intent.attacker]
                defender_pos = world.components['Position'][target_eid]
                from_left = attacker_pos.x < defender_pos.x
                fsm = world.components['NPCState'][target_eid].fsm
                proxy = _EntityProxy(world, target_eid)
                # usar DamageState para pausa y luego ChaseState
                fsm.change_state(DamageState(ChaseState(), from_left), proxy)
            
            # (Opcional) Aquí podrías disparar efectos secundarios,
            # p.ej. animaciones, sonidos o eventos de muerte si HP ≤ 0
            
            # Limpia el evento para no reprocesarlo
            del world.components['WantsToMelee'][eid]
