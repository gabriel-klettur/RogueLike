



# Propuesta: 10 nuevos Types de hechizos (con “puddle/poso” y 9 más)

Asumo que quieres “Types” nuevos (categorías de hechizos) integrados a tu runtime: [resolvers_pkg](cci:7://file:///d:/Python/RogueLike/src/roguelike_game/ecs/systems/combat/spells/resolvers_pkg:0:0-0:0), sistemas ECS y [spells.json](cci:7://file:///d:/Python/RogueLike/data/spells/spells.json:0:0-0:0) con hot‑reload. Te propongo 10 Types viables y distintos, con su resolver, componentes, sistemas y campos de config. El primero es tu “poso” circular (puddle) con variantes por asset: agua, veneno, ácido, lava, hielo.

## Lista final de Types propuestos

- **[1] puddle**
- **[2] wall**
- **[3] mine**
- **[4] chain_lightning**
- **[5] summon**
- **[6] cone_breath**
- **[7] vortex_field** (pull/push)
- **[8] meteor_shower**
- **[9] totem**
- **[10] boomerang**

Cada Type incluye:
- Resolver en `resolvers_pkg/<type>.py`
- Componentes nuevos
- System(s) de update y, si aplica, de render
- Snippet mínimo de [spells.json](cci:7://file:///d:/Python/RogueLike/data/spells/spells.json:0:0-0:0)
- Campos `effect.*` y `vfx.*` coherentes con tu aplanado actual

---

## [1] Type: puddle (poso/charco circular)

- **Resolver**: `PuddleResolver`
- **Componentes**:
  - `PuddleComponent(radius, duration, tick_period, damage|heal, status, move_speed_mult, element, owner)`
  - Opcional: `DecalSprite/Scale` para piso o `ParticleEmitter` preset “puddle”
- **Sistemas**:
  - `PuddleSystem` (tick periódico: aplica daño/curación/slow; expira)
  - `PuddleRenderSystem` (si usas decal/sprite semitransparente)
- **Campos effect**:
  - `radius`, `duration`, `tick_period`
  - `damage` o `heal`
  - `status` (e.g., "burn", "poison", "freeze"), `move_speed_mult`, `element` ("water","poison","acid","lava","ice")
- **VFX**:
  - `vfx.sprite.path` para textura del charco; `vfx.sprite.scale`
  - O `vfx.preset: "puddle_emitter_*"`
- **Snippet spells.json**:
```json
"puddle_lava": {
  "id": "puddle_lava",
  "type": "puddle",
  "mana_cost": 5,
  "timings": { "prepare": 0.1, "channel": 0.0, "cooldown": 0.6 },
  "rules": { "lock_cast_direction": true, "interruptible": true, "allow_movement": false },
  "constraints": { "max_instances": 3, "allow_overlap": true },
  "effect": {
    "radius": 90, "duration": 6.0, "tick_period": 0.35,
    "damage": 4, "status": "burn", "move_speed_mult": 0.9, "element": "lava"
  },
  "vfx": { "sprite": { "path": "assets/areas/puddles/lava.png", "scale": 0.8 } }
}
```
- Variantes (agua/veneno/ácido/hielo) cambian `sprite` y `status`/`move_speed_mult`.

---

## [2] Type: wall (muro temporal)

- **Resolver**: `WallResolver`
- **Componentes**:
  - `WallSegmentComponent(duration, hp, width, length, blocks_projectiles, blocks_units)`
  - `Collider/Obstacle` por segmento
- **Sistemas**:
  - `WallSystem` (vida/expiración), integrado con colisiones de movimiento y proyectiles
  - `WallRenderSystem` (sprite o tiles)
- **effect**: `duration`, `hp`, `length`, `width`, flags `blocks_projectiles`, `blocks_units`
- **Snippet**:
```json
"wall_ice": {
  "id": "wall_ice", "type": "wall", "mana_cost": 12,
  "timings": { "prepare": 0.2, "channel": 0.0, "cooldown": 4.0 },
  "effect": { "duration": 6.0, "hp": 50, "length": 240, "width": 24, "blocks_projectiles": true, "blocks_units": true },
  "vfx": { "preset": "wall_ice" }
}
```

---

## [3] Type: mine (trampa/minefield)

- **Resolver**: `MineResolver`
- **Componentes**:
  - `MineComponent(trigger_radius, arming_time, payload, ttl)`
- **Sistemas**:
  - `MineSystem` (arma, detecta entrada de enemigos, dispara payload, expira)
  - Usa `ExplosionSystem` para payloads de explosión
- **effect**: `trigger_radius`, `arming_time`, `payload` (e.g., { "explosion": { "radius": 120, "damage": 24 } }), `ttl`
- **Snippet**:
```json
"mine_basic": {
  "id": "mine_basic", "type": "mine", "mana_cost": 6,
  "timings": { "prepare": 0.0, "channel": 0.0, "cooldown": 0.8 },
  "effect": { "trigger_radius": 60, "arming_time": 0.5, "ttl": 14,
    "payload": { "explosion": { "radius": 140, "damage": 28 } } },
  "vfx": { "preset": "mine_glow" }
}
```

---

## [4] Type: chain_lightning (rebota entre objetivos)

- **Resolver**: `ChainLightningResolver`
- **Componentes**:
  - `ChainLightningComponent(max_bounces, range, damage_decay, targets_hit)`
- **Sistemas**:
  - `ChainLightningSystem` (salta a próximos objetivos, aplica daño; usa tu `LightningRenderSystem` para rayos)
- **effect**: `max_bounces`, `range`, `damage`, `damage_decay`
- **Snippet**:
```json
"chain_lightning": {
  "id": "chain_lightning", "type": "chain_lightning", "mana_cost": 14,
  "timings": { "prepare": 0.1, "channel": 0.1, "cooldown": 1.4 },
  "constraints": { "max_instances": 1, "allow_overlap": true },
  "effect": { "damage": 12, "max_bounces": 4, "range": 360, "damage_decay": 0.8 },
  "vfx": { "preset": "lightning_chain" }
}
```

---

## [5] Type: summon (invoca aliado)

- **Resolver**: `SummonResolver`
- **Componentes**:
  - `SummonedUnitComponent(owner, duration, template_id)`
- **Sistemas**:
  - `SummonSystem` (vida, expiración; delega al pipeline de spawn/AI existente por `template_id`)
- **effect**: `duration`, `template_id`, opcional `count`, `spread_radius`
- **Snippet**:
```json
"summon_wolf": {
  "id": "summon_wolf", "type": "summon", "mana_cost": 20,
  "timings": { "prepare": 0.3, "channel": 0.0, "cooldown": 8.0 },
  "effect": { "template_id": "ally_wolf", "duration": 20.0, "count": 1 },
  "vfx": { "preset": "summon_portal" }
}
```

---

## [6] Type: cone_breath (cono continuo anclado al caster)

- **Resolver**: `ConeBreathResolver`
- **Componentes**:
  - `ConeBreathComponent(arc_deg, length, dps, tick_period, element)`
  - Reutiliza `HitboxSystem` en modo cono o un sistema propio
- **Sistemas**:
  - `ConeBreathSystem` (aplica tick a enemigos dentro del cono, sigue al caster)
- **effect**: `arc_range_degrees`, `length`, `dps` o `damage_per_tick`, `tick_period`, `element`
- **Snippet**:
```json
"flame_breath": {
  "id": "flame_breath", "type": "cone_breath", "mana_cost": 12,
  "timings": { "prepare": 0.1, "channel": 0.2, "cooldown": 1.6 },
  "effect": { "arc_range_degrees": 60, "length": 260, "damage_per_tick": 4, "tick_period": 0.2, "element": "fire" },
  "vfx": { "preset": "breath_fire" }
}
```

---

## [7] Type: vortex_field (campo de fuerza: pull/push)

- **Resolver**: `VortexFieldResolver`
- **Componentes**:
  - `ForceFieldComponent(radius, force, mode: "pull"|"push", duration)`
- **Sistemas**:
  - `ForceFieldSystem` (aplica fuerzas a `Velocity` de entidades dentro del radio)
- **effect**: `radius`, `force`, `mode`, `duration`
- **Snippet**:
```json
"vortex_pull": {
  "id": "vortex_pull", "type": "vortex_field", "mana_cost": 10,
  "timings": { "prepare": 0.1, "channel": 0.0, "cooldown": 2.0 },
  "effect": { "radius": 280, "force": 1400, "mode": "pull", "duration": 2.2 },
  "vfx": { "preset": "vortex_dark" }
}
```

---

## [8] Type: meteor_shower (lluvia de meteoros programada)

- **Resolver**: `MeteorShowerResolver`
- **Componentes**:
  - `ScheduledSpawnerComponent(count, interval, area_radius, projectile_cfg)`
- **Sistemas**:
  - `ScheduledSpawnSystem` (dispara spawns; cada meteoro al impactar usa `ExplosionSystem`)
- **effect**: `count`, `interval`, `area_radius`, `impact_damage`, `impact_radius`
- **Snippet**:
```json
"meteor_shower": {
  "id": "meteor_shower", "type": "meteor_shower", "mana_cost": 18,
  "timings": { "prepare": 0.3, "channel": 0.2, "cooldown": 6.0 },
  "effect": { "count": 8, "interval": 0.25, "area_radius": 520,
              "impact_damage": 30, "impact_radius": 160 },
  "vfx": { "preset": "meteor_trail" }
}
```

---

## [9] Type: totem (emisor estacionario de aura/efecto)

- **Resolver**: `TotemResolver`
- **Componentes**:
  - `TotemComponent(duration, tick_period, radius, effect_kind, values...)`
- **Sistemas**:
  - `TotemSystem` (ticks periódicos que emiten aura buff/debuff, o proyectiles lentos)
- **effect**: `duration`, `tick_period`, `radius`, `kind` ("heal","damage","regen","slow"…) y parámetros
- **Snippet**:
```json
"healing_totem": {
  "id": "healing_totem", "type": "totem", "mana_cost": 16,
  "timings": { "prepare": 0.2, "channel": 0.0, "cooldown": 8.0 },
  "effect": { "duration": 10.0, "tick_period": 0.5, "radius": 220,
              "kind": "heal", "heal_per_tick": 6 },
  "vfx": { "preset": "totem_heal" }
}
```

---

## [10] Type: boomerang (proyectil que vuelve)

- **Resolver**: `BoomerangResolver`
- **Componentes**:
  - `BoomerangComponent(speed, damage, range, return_speed, passes_through)`
- **Sistemas**:
  - `BoomerangSystem` (ida y vuelta; colisiones en ambos trayectos)
  - Render puede reutilizar el de proyectil
- **effect**: `damage`, `range`, `speed`, `return_speed`, `passes_through`
- **Snippet**:
```json
"boomerang": {
  "id": "boomerang", "type": "boomerang", "mana_cost": 8,
  "timings": { "prepare": 0.05, "channel": 0.0, "cooldown": 0.8 },
  "constraints": { "max_instances": 1, "allow_overlap": true },
  "effect": { "damage": 18, "range": 420, "speed": 22, "return_speed": 28, "passes_through": false },
  "vfx": { "sprite": { "path": "assets/projectiles/boomerang.png", "scale": 0.8 }, "preset": "boomerang_trail" }
}
```

---

## Esquema común y encaje en tu arquitectura

- **rules.***: sigue tus llaves (lock_cast_direction, interruptible, allow_movement, automatic, automatic_cast_punish).
- **constraints.***: `max_instances`, `allow_overlap`.
- **timings.***: `prepare`, `channel`, `cooldown` usados por la sub‑FSM.
- **effect.***: campos por Type (definidos arriba).
- **vfx.***: `preset` de partículas, y/o `sprite.path` + `sprite.scale`.
- **Integración**:
  - Añadir resolvers a [resolvers_pkg/registry.py](cci:7://file:///d:/Python/RogueLike/src/roguelike_game/ecs/systems/combat/spells/resolvers_pkg/registry.py:0:0-0:0) → `SPELL_RESOLVERS['<type>'] = ...`.
  - Crear Systems y registrarlos en [ecs/core/system_registry.py](cci:7://file:///d:/Python/RogueLike/src/roguelike_game/ecs/core/system_registry.py:0:0-0:0) cerca de sus pares (p.ej., `PuddleSystem` antes de `HitboxSystem` si aplica daño por área; `MineSystem` antes de `ExplosionSystem`).
  - Si algún Type requiere aplanar nuevos campos de partículas, extender el mapeo en [spells_config.py](cci:7://file:///d:/Python/RogueLike/src/roguelike_game/config/spells_config.py:0:0-0:0) (similar a cómo aplanas `vfx.particles.*` y `vfx.sprite.*`). Si los nuevos sistemas leen `cfg.effect.*` directamente del objeto tipado, no hace falta aplanar extra.

---

## Recomendaciones de implementación incremental

- **Primero**: `puddle` (rápido, muy visible). Reutiliza parte de `HitboxSystem` si quieres daño por “presencia” con un `AreaHitboxComponent`; si prefieres tick fijo, implementa `PuddleSystem`.
- **Segundo**: `mine` y `boomerang` (diversifican gameplay sin gran costo).
- **Tercero**: `chain_lightning` y `vortex_field` (AI/targets y fuerzas).
- **Luego**: `cone_breath`, `meteor_shower`, `totem`, `wall`, `summon`.

---

## Key Terminology (glosario rápido)

- **Type (tipo de hechizo)** — Categoría que decide resolver y sistemas — Úsalo para mapear `cfg.type` → lógica — ej.: `type: "puddle"`.
- **Resolver** — Traductor de config a componentes ECS — Lo invoca [ReleaseSpellState](cci:2://file:///d:/Python/RogueLike/src/roguelike_game/ecs/systems/fsm/states/spell/release_spell_state.py:15:0-195:12) — ej.: [PuddleResolver.resolve(...)](cci:1://file:///d:/Python/RogueLike/src/roguelike_game/ecs/systems/combat/spells/resolvers_pkg/aura.py:7:4-22:71).
- **Area tick** — Daño/curación periódica en un área — Úsalo para auras/posos — ej.: cada 0.25s aplicar daño.
- **Force field** — Campo que aplica fuerzas — Úsalo para atraer/empujar — ej.: `force = 1400` hacia centro.
- **Scheduled spawn** — Spawns por intervalo — Útil para meteoros — ej.: 8 impactos cada 0.25s.
- **Decal** — Sprite pegado al suelo — Úsalo para charcos y paredes — ej.: `vfx.sprite.path`.
- **Payload** — Efecto encapsulado que disparamos — Úsalo en minas — ej.: `payload.explosion`.

---

## Cómo defender esta propuesta

- **Objetivo y criterios**: 10 Types nuevos, distintos y combinables; compatibles con tu FSM, resolvers y hot‑reload.
- **Diseño**: patrón “resolver + system” mantiene baja acoplación; los datos viven en [spells.json](cci:7://file:///d:/Python/RogueLike/data/spells/spells.json:0:0-0:0).
- **Rendimiento**: los systems aplican lógica por frame/tick solo a entidades del Type; VFX vía presets existentes.
- **Extensibilidad**: cada Type escala con nuevas variantes (IDs) sin tocar código; assets configurables por `vfx`.
- **Riesgos**: overlap de daños (stacking) y orden de systems; mitigación con `constraints.max_instances`, `allow_overlap` y orden en `system_registry`.
- **Siguientes pasos**: implementar `puddle` end‑to‑end, registrar en [registry.py](cci:7://file:///d:/Python/RogueLike/src/roguelike_game/ecs/systems/combat/spells/resolvers_pkg/registry.py:0:0-0:0) y [system_registry.py](cci:7://file:///d:/Python/RogueLike/src/roguelike_game/ecs/core/system_registry.py:0:0-0:0), agregar un ejemplo a [spells.json](cci:7://file:///d:/Python/RogueLike/data/spells/spells.json:0:0-0:0), probar hot‑reload (F4).

¿Confirmas esta lista de 10 Types? Si te parece bien, empiezo implementando “puddle” primero y te dejo listo un hechizo `puddle_lava` con su resolver, system y render mínimo.