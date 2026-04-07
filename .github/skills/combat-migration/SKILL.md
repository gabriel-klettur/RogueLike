---
name: combat-migration
description: "Migrate combat systems from Python to Unity. Use when porting melee, spells, projectiles, hitboxes, damage, knockback, death, explosions, status effects, or combo systems. Covers CombatSystem, MeleeSystem, HitboxSystem, SpellCaster, FSM combat states."
argument-hint: "Name the combat subsystem to migrate (melee, spells, hitbox, damage, etc.)"
---

# Combat System Migration

## When to Use
- Porting melee combat (slash, attack, cooldown)
- Porting spell/projectile systems
- Porting hitbox detection and damage pipeline
- Porting knockback, death, respawn mechanics
- Porting status effects (burn, stun, etc.)
- Porting combo system

## Python Combat Architecture

### Source Files
```
python/src/roguelike_game/ecs/systems/combat/
├── combat_system.py          # Damage application, knockback
├── death_system.py           # Entity death/respawn
├── explosions.py             # AOE damage
├── explosion_system.py       # Explosion processing
├── combat_sfx.py             # Hit sounds
├── burn_system.py            # DoT status effect
├── hitbox/
│   └── hitbox_system.py      # Collision detection for attacks
├── melee/
│   ├── melee_system.py       # Basic melee attacks
│   └── slash_system.py       # Slash arc detection
└── spells/
    └── [spell-specific systems]
```

### Key Components
```
CombatStats: {current_hp, max_hp, power, defense}
MeleeWeapon: {damage, cooldown}
WantsToMelee: {target_id}
WantsToCastSpell: {spell_id}
AttackCooldown: {remaining_time}
Health, Mana, Energy
DeathTimer: {countdown}
```

### Damage Formula (MUST PRESERVE)
```python
# From combat_system.py - read actual values before porting
raw_damage = attacker.power + weapon.damage
mitigated = max(1, raw_damage - target.defense)
# Knockback is applied as velocity impulse
```

## Unity Combat Architecture

### Existing Scripts
```
Assets/_Project/Scripts/Gameplay/Combat/
├── Health.cs                  # HP system
├── Mana.cs                    # Mana system
├── Experience.cs              # XP/leveling
├── MeleeCombat.cs             # Melee attack resolution
├── DashAbility.cs             # Dash mechanics
├── CombatFeedback.cs          # Hit feedback (knockback, stun)
├── CombatRangeVisualizer.cs   # Range indicator
├── FacingIndicator.cs         # Direction indicator
├── FloatingDamageNumber.cs    # Damage popup
├── FloatingDamageSpawner.cs   # Damage popup spawner
├── MouseTargetDetector.cs     # Mouse targeting
├── WorldHealthBar.cs          # HP bar above entities
```

### Spell Scripts
```
Assets/_Project/Scripts/Gameplay/Spells/
├── SpellCaster.cs             # Main casting system
├── ISpellExecutor.cs          # Interface
├── ProjectileExecutor.cs      # Projectile spells
├── AreaExecutor.cs            # AoE spells
├── SlashExecutor.cs           # Slash spells
├── DashExecutor.cs            # Dash spells
├── Projectile.cs              # Projectile physics
├── FireballVisual.cs          # Fireball VFX
```

## Migration Procedure

1. **Read Python system** — extract exact damage formula, timing values, ranges
2. **Read Python tests** — understand expected behavior for edge cases
3. **Read existing Unity combat scripts** — identify what's already ported
4. **Compare** — create a delta list of missing features
5. **Implement** — port missing features preserving numerical parity
6. **Validate** — run Unity tests, compare with Python test expectations

## Critical Values to Preserve

These come from JSON configs — always read the actual values:

| Value | Source |
|-------|--------|
| Melee damage | `new_hostiles.json` → `melee_damage` |
| Melee cooldown | `new_hostiles.json` → `melee_cooldown` |
| Melee range | `new_hostiles.json` → `melee_range` |
| Spell damage | `spells.json` → `effect.damage` |
| Spell speed | `spells.json` → `effect.speed` |
| Spell cooldown | `spells.json` → `timings.cooldown` |
| Spell range | `spells.json` → `effect.range` |
| Knockback force | `combat_system.py` → knockback calculation |
| Defense mitigation | `combat_system.py` → damage formula |

## Layer Collision Rules (FIX NEEDED - P2.1)

Correct matrix:
```
Player ↔ [NPC, World, Building, Pickup]
NPC ↔ [Player, NPC, World, Building, Projectile]
Projectile ↔ [NPC, World, Building]  (NOT Player — prevents self-hit)
Pickup ↔ [Player only]
Spawner → Non-physical (trigger only)
UIBlocker → Non-physical (trigger only)
```
