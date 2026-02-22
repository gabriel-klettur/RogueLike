"""Centralized combat SFX configuration for damage-received and attack sounds.

Data-driven mapping from archetype prefixes to their sfx_id choices.
New NPC types only need an entry here and the corresponding assets
registered in ``data/config/audio.json`` to get automatic SFX support.

Design:
  - *Separation of concerns*: keeps audio-mapping logic out of HitboxSystem
    and AttackState, which remain focused on collision/FSM respectively.
  - *Open/Closed*: adding a new NPC archetype requires zero code changes
    in the combat systems — just extend the dicts below.
"""

from __future__ import annotations

from typing import Dict, List, Optional

# ---------------------------------------------------------------------------
# Player damage SFX choices (random selection at runtime)
# ---------------------------------------------------------------------------
PLAYER_DAMAGE_CHOICES: List[str] = [
    f"player_damage_{i}" for i in range(1, 23)
]

# ---------------------------------------------------------------------------
# NPC damage-received SFX — keyed by archetype *prefix* (lowercase).
# The first prefix that matches ``archetype.type.lower().startswith(key)``
# wins.  Order does NOT matter because we iterate longest-prefix-first.
# ---------------------------------------------------------------------------
NPC_DAMAGE_SFX: Dict[str, List[str]] = {
    "barbol": ["barbol_damage_1"],
}

# ---------------------------------------------------------------------------
# NPC attack SFX — same prefix-matching logic as damage.
# ---------------------------------------------------------------------------
NPC_ATTACK_SFX: Dict[str, List[str]] = {
    "barbol": ["barbol_attack_1", "barbol_attack_2", "barbol_attack_3"],
}

# Pre-sorted keys longest-first for deterministic prefix matching
_DAMAGE_KEYS_SORTED: List[str] = sorted(NPC_DAMAGE_SFX.keys(), key=len, reverse=True)
_ATTACK_KEYS_SORTED: List[str] = sorted(NPC_ATTACK_SFX.keys(), key=len, reverse=True)


def resolve_npc_damage_choices(archetype_type: Optional[str]) -> Optional[List[str]]:
    """Return the list of sfx_id choices for an NPC that received damage.

    Returns ``None`` when no mapping exists for the given archetype.
    """
    if not archetype_type:
        return None
    lower = str(archetype_type).lower()
    for prefix in _DAMAGE_KEYS_SORTED:
        if lower.startswith(prefix):
            return NPC_DAMAGE_SFX[prefix]
    return None


def resolve_npc_attack_choices(archetype_type: Optional[str]) -> Optional[List[str]]:
    """Return the list of sfx_id choices for an NPC attack action.

    Returns ``None`` when no mapping exists for the given archetype.
    """
    if not archetype_type:
        return None
    lower = str(archetype_type).lower()
    for prefix in _ATTACK_KEYS_SORTED:
        if lower.startswith(prefix):
            return NPC_ATTACK_SFX[prefix]
    return None
