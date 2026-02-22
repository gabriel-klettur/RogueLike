# Descripción completa de NPCs (news_specials.json)

Este documento describe el funcionamiento de los NPCs definidos en `data/entities/news_specials.json`, incluyendo sus ataques, magias, comportamiento y transiciones de fase.

- **Fuente**: `data/entities/news_specials.json`
- **Conjunto**: `specials.classes`
- **NPCs**: 3 clases/fases de Final Boss Barbol (I, II, III)

## Convenciones y términos

- **Aggro range**: distancia a la que detecta al jugador para iniciar persecución o combate.
- **Melee**: ataque cuerpo a cuerpo; usa `melee_range`, `melee_damage`, `melee_cooldown`.
- **Auto-cast**: lista de hechizos que el NPC lanza de forma automática según sus periodos (`period_s` o rangos `min/max_period_s`).
- **Channel**: tiempo de canalización previo a lanzar el hechizo (`channel_s`).
- **Chasing speed**: velocidad usada durante persecución (`chasing_speed`).
- **FSM set**: conjunto de estados y transiciones del NPC (aquí: `Monster_Default`).
- **Phase/next_phase**: fase actual y la siguiente fase del jefe.

---

## Final Boss Barbol I (`final_boss_barbol`)

- **Nombre**: Final Boss Barbol I
- **Fase**: 1 → siguiente: `final_boss_barbol_lvl2`
- **FSM**: `Monster_Default`
- **Patrulla**: `line`

### Ataques y magias

- **Ataques (1)**
  - Melee: daño 50, alcance 7, enfriamiento 1.0 s, preparación de ataque (`attack_windup_s`) 2.0 s.
- **Magias (1)**
  - `root_whip`: objetivo `player`.
    - Periodicidad: aleatoria entre 5.0 y 10.0 s.
    - Canalización: 2.0 s.
    - Efecto visual: `wire_from` [0,128,255] → `wire_to` [0,255,0].

### Estadísticas clave

- HP 2500, Speed 1.5, Chasing speed 3.5
- Aggro range 20
- Defensa 8, Poder 14
- Duración daño 0.5 s, Prob. detenerse al recibir daño 0.05
- Tamaño pies: width 0.5, height 0.22
- Spawn: count 1, padding 3, margin 0
- Desaparición al morir: 0.2 s

### Comportamiento (derivado de config)

- Patrulla en línea hasta detectar al jugador dentro de `aggro_range`.
- Persigue usando `chasing_speed` y ataca en melee cuando el jugador está dentro de `melee_range`.
- Lanza `root_whip` automáticamente respetando su canalización y periodo aleatorio.

---

## Final Boss Barbol II (`final_boss_barbol_lvl2`)

- **Nombre**: Final Boss Barbol II
- **Fase**: 2 → siguiente: `final_boss_barbol_lvl3`
- **FSM**: `Monster_Default`
- **Patrulla**: `line`

### Ataques y magias

- **Ataques (1)**
  - Melee: daño 60, alcance 7, enfriamiento 0.9 s.
- **Magias (2)**
  - `root_whip`: igual que Fase I (periodo 5–10 s, canalización 2.0 s, objetivo `player`).
  - `fireball`:
    - Periodicidad fija: cada 2.0 s.
    - `scale_multiplier`: 2.5.
    - Radio de impacto (`hit_radius`): 6.0.
    - Daño: 30.

### Estadísticas clave

- HP 3000, Speed 1.6, Chasing speed 3.8
- Aggro range 22
- Mana 10000 / 10000
- Defensa 10, Poder 16
- Duración daño 0.5 s, Prob. detenerse al recibir daño 0.04
- Tamaño pies: width 0.5, height 0.22
- Spawn: count 1, padding 3, margin 0
- Desaparición al morir: 0.2 s

### Comportamiento (derivado de config)

- Similar a Fase I, con mejora de estadísticas y una magia adicional (`fireball`).
- Combinación de auto-casts: `root_whip` (aleatorio) + `fireball` (periódico cada 2 s).

---

## Final Boss Barbol III (`final_boss_barbol_lvl3`)

- **Nombre**: Final Boss Barbol III
- **Fase**: 3 → no hay siguiente (`next_phase: null`)
- **FSM**: `Monster_Default`
- **Patrulla**: `line`

### Ataques y magias

- **Ataques (1)**
  - Melee: daño 70, alcance 8, enfriamiento 0.85 s.
- **Magias (2)**
  - `root_whip`: igual base (periodo 5–10 s, canalización 2.0 s, objetivo `player`).
  - `fireball` (potenciado):
    - Periodicidad fija: cada 2.0 s.
    - `scale_multiplier`: 5.0.
    - Radio de impacto: 10.0.
    - Daño: 60.

### Estadísticas clave

- HP 4000, Speed 1.7, Chasing speed 4.0
- Aggro range 24
- Mana 10000 / 10000
- Defensa 12, Poder 18
- Duración daño 0.5 s, Prob. detenerse al recibir daño 0.03
- Tamaño pies: width 0.5, height 0.22
- Spawn: count 1, padding 3, margin 0
- Desaparición al morir: 0.2 s

### Comportamiento (derivado de config)

- Variante más agresiva: melee más fuerte y mayor alcance, persecución más rápida, y `fireball` reforzado.

---

## Comparativa entre fases (resumen)

- **HP**: 2500 → 3000 → 4000
- **Speed / Chasing**: 1.5/3.5 → 1.6/3.8 → 1.7/4.0
- **Aggro range**: 20 → 22 → 24
- **Melee (daño/alcance/cd)**: 50/7/1.0 → 60/7/0.9 → 70/8/0.85
- **Defensa/Poder**: 8/14 → 10/16 → 12/18
- **Auto-casts**: 1 (`root_whip`) → 2 (`root_whip` + `fireball`) → 2 (`root_whip` + `fireball` potenciado)
- **Prob. detenerse al daño**: 0.05 → 0.04 → 0.03

## Notas adicionales

- `attack_windup_s` solo aparece explícitamente en Fase I (2.0 s).
- `fireball` escala en Fase II y Fase III (más tamaño/área y daño en F3).
- `wire_from`/`wire_to` de `root_whip` sugieren un efecto visual de “enraizar/latigazo”.

## Glosario rápido

- **Aggro range (rango de agresión)** — Distancia a la que un NPC entra en combate — Úsalo para controlar cuándo inicia la persecución — p.ej., 22 significa que detecta al jugador a 22 unidades.
- **Auto-cast** — Hechizo lanzado automáticamente por IA — Útil para jefes con patrones — p.ej., `fireball` cada 2 s.
- **Channel (canalización)** — Tiempo previo obligatorio antes de lanzar — Útil para telégrafos — p.ej., `root_whip` tarda 2.0 s.
- **Cooldown (enfriamiento)** — Tiempo mínimo entre acciones repetibles — Controla la cadencia — p.ej., melee 1.0 s.
- **FSM (máquina de estados)** — Modelo de estados y transiciones — Ordena la IA — p.ej., Idle → Chase → Attack → Damage.
- **Chasing speed (velocidad de persecución)** — Velocidad durante la persecución — Ajusta la presión — p.ej., 4.0 en Fase III.
- **Hit radius (radio de impacto)** — Área afectada por un hechizo — Dimensiona AOE — p.ej., 10.0 en Fase III.
- **Phase (fase)** — Etapa del jefe con stats/patrones — Permite progresión — p.ej., Fase II → Fase III.

