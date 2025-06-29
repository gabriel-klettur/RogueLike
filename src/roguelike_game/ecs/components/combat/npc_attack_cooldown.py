# Path: src/roguelike_game/ecs/components/combat/npc_attack_cooldown.py
from dataclasses import dataclass

@dataclass
class NPCAttackCooldown:
    """
    Controla el tiempo en que un NPC puede volver a dañar al jugador.
    next_time: timestamp de la próxima vez en que puede golpear.
    """
    next_time: float