from dataclasses import dataclass


@dataclass
class ComboRulesComponent:
    """
    Reglas de filtrado para qué impactos cuentan en el combo.

    - allowed_sources: dict con claves 'melee', 'hitbox', 'fireball', 'spell'...
    - min_damage: daño mínimo para contar.
    - require_enemy: si True, solo cuenta contra entidades que no sean jugador.
    - require_unique_target: si True, exige alternar de objetivo (no contar dos hits seguidos al mismo target).
    """
    allowed_sources: dict
    min_damage: float = 0.0
    require_enemy: bool = True
    require_unique_target: bool = False
