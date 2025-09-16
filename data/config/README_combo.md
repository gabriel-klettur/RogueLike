# Sistema de Combos (Hit Combo Counter)

Este documento describe la arquitectura, configuración y funcionamiento del sistema de combos del jugador. El objetivo es medir impactos consecutivos dentro de una ventana de tiempo ("combo window"), mostrar una barra visual en el HUD y aplicar dificultad progresiva para mantener el combo.

---

## Arquitectura general

El sistema está integrado en el ECS/FSM y se basa en eventos:

- __Eventos de combate__ publican `OnHit/OnDeath` a `FSMEventQueue` (flujo ya existente) y además eventos de combo a `ComboEventQueue` cuando el jugador golpea a un enemigo.
- __ComboSystem__ consume `ComboEventQueue` y gestiona el estado del combo (contador, temporizador, reglas y dificultad progresiva), además de escuchar eventos de ruptura (`break`) para reiniciar el combo cuando el jugador recibe daño.
- __ComboBarRenderSystem__ dibuja una barra de tiempo y el multiplicador `xN` en el HUD, alineada con la barra de experiencia. También puede mostrar un flash/fade al romperse el combo y un contador de combos completados.

Archivos relevantes:
- `src/roguelike_game/ecs/components/abilities/combo_counter_component.py`
- `src/roguelike_game/ecs/components/abilities/combo_rules_component.py`
- `src/roguelike_game/ecs/systems/abilities/combo_system.py`
- `src/roguelike_game/ecs/systems/rendering/combo_bar_render_system.py`
- `src/roguelike_game/ecs/core/system_registry.py` (orden de sistemas)
- `src/roguelike_game/factories/player/builder.py` (defaults del jugador)
- `data/config/combo_rules.json` (config JSON con hot‑reload)

---

## Componentes

### `ComboCounterComponent`
Propiedades principales:
- `current: int` — hits consecutivos del combo actual.
- `best: int` — récord máximo alcanzado.
- `window_s: float` — ventana base de tiempo para mantener el combo.
- `window_end_time: float` — instante (epoch) en que expira el combo si no hay nuevo hit.
- `last_hit_time_by_target: Dict[int, float]` — anti‑spam por objetivo (evita múltiples conteos inmediatos para el mismo target).
- `same_target_cooldown_s: float` — tiempo mínimo entre conteos contra el mismo target.
- `last_target_id: Optional[int]` — último objetivo impactado (para reglas de alternancia).
- `min_window_s: float` — ventana mínima al aplicar dificultad progresiva.
- `difficulty_increase_per_hit: float` — incremento de dificultad por impacto (reduce ventana).
- `last_window_start_time: float` — para calcular correctamente el progreso visual.
- `last_window_duration: float` — duración efectiva de la última ventana (tras aplicar dificultad).
- `break_flash_duration_s: float` — duración del flash/fade al romper combo.
- `break_flash_end_time: float` — instante hasta el cual se muestra el flash/fade.
- `total_completed: int` — número de combos completados en la sesión.
- `last_completed_count: int` — tamaño del último combo finalizado.

Métodos clave:
- `_effective_window_for_count(n: int) -> float` — devuelve la ventana efectiva tras aplicar dificultad progresiva y `min_window_s`.
- `on_valid_hit(target_eid, at_time)` — incrementa `current`, refresca ventana efectiva y marca tiempos auxiliares.
- `reset()` — resetea el estado del combo.

### `ComboRulesComponent`
- `allowed_sources: dict` — claves como `melee`, `hitbox`, `fireball` para permitir/denegar fuentes.
- `min_damage: float` — daño mínimo para contar.
- `require_enemy: bool` — exige que el target no sea el jugador.
- `require_unique_target: bool` — obliga a alternar objetivos (combo más técnico).

---

## Sistemas

### `ComboSystem`
Responsabilidades:
- Consumir `world.components['ComboEventQueue']`.
- Incrementar el combo en eventos de golpe válidos y aplicar reglas/filtros.
- Romper el combo en eventos `{'type': 'break', 'entity': player_eid}` (se publican al recibir daño).
- Gestionar expiración de ventana y contadores de combos completados.
- Hot‑reload de `data/config/combo_rules.json` (≈1s) para ajustar dinámicamente parámetros del jugador.

Estructura típica de un evento de combo (golpe):
```json
{
  "attacker": <eid_jugador>,
  "target": <eid_npc>,
  "damage": 12.0,
  "source": "melee" | "hitbox" | "fireball",
  "time": 1725610000.123
}
```

Estructura de un evento de ruptura:
```json
{ "type": "break", "entity": <eid_jugador>, "time": 1725610000.123 }
```

### `ComboBarRenderSystem`
- Dibuja una barra horizontal por encima de la barra de EXP, con el mismo ancho.
- El relleno representa el tiempo restante de la ventana efectiva actual.
- Muestra `xN` a la derecha (N = hits actuales) y `Combos <n>` encima a la izquierda.
- En ruptura reciente, muestra flash/fade blanco durante `break_flash_duration_s`.

---

## Configuración JSON (hot‑reload)
Archivo: `data/config/combo_rules.json`

Claves en `player`:
- `window_s: float` — ventana base (segundos).
- `min_window_s: float` — ventana mínima con dificultad progresiva.
- `difficulty_increase_per_hit: float` — porcentaje de reducción por impacto (0.05 = 5%).
- `break_flash_duration_s: float` — duración del flash al romper.
- `same_target_cooldown_s: float` — enfriamiento entre impactos al mismo objetivo.
- `rules.allowed_sources: { str: bool }` — habilita fuentes válidas (`melee`, `hitbox`, `fireball`).
- `rules.min_damage: float` — daño mínimo para contar.
- `rules.require_enemy: bool` — true para ignorar golpes al jugador.
- `rules.require_unique_target: bool` — true para obligar alternar objetivos.

Ejemplo:
```json
{
  "player": {
    "window_s": 2.0,
    "min_window_s": 0.3,
    "difficulty_increase_per_hit": 0.05,
    "break_flash_duration_s": 0.3,
    "same_target_cooldown_s": 0.5,
    "rules": {
      "allowed_sources": { "melee": true, "hitbox": true, "fireball": true },
      "min_damage": 1.0,
      "require_enemy": true,
      "require_unique_target": true
    }
  }
}
```

Hot‑reload:
- El `ComboSystem` recarga el JSON ~cada 1 segundo si el archivo cambió. No requiere reiniciar el juego.

---

## Flujo del combo
1. __Golpe válido__ (jugador → enemigo):
   - Pasa filtros (`allowed_sources`, `min_damage`, alternancia, anti‑spam).
   - `current += 1` y se refresca la ventana a la duración efectiva (`window_s * (1 - diff)^(N-1)`, limitado por `min_window_s`).
2. __Sin nuevos golpes antes de expirar__: el combo finaliza y se registra en `total_completed`/`last_completed_count`.
3. __Jugador recibe daño__: se publica evento `break`, se registra el combo si procede y se resetea. Se muestra flash/fade.

---

## Dificultad progresiva
- Fórmula de ventana efectiva para un combo de longitud `N`:
  ```
  window_eff(N) = max(min_window_s, window_s * (1 - difficulty_increase_per_hit)^(N - 1))
  ```
- Con `difficulty_increase_per_hit = 0.05`, cada hit reduce la ventana un 5% (hasta `min_window_s`).

---

## Efectos visuales
- __Flash/fade de ruptura__: overlay blanco sobre la barra de combo, con alpha que decrece linealmente durante `break_flash_duration_s`.
- Se puede ajustar color/duración en el renderer y en el JSON.

---

## Estadísticas
- `best` — récord de hits en un combo durante la sesión.
- `total_completed` — número de combos completados.
- `last_completed_count` — tamaño del último combo completado.

---

## Extensiones sugeridas
- Mostrar el récord (`best`) al lado de `xN`.
- Pulsos visuales en cada hit (no solo en ruptura).
- Bonificaciones por rachas de combos (XP, oro, daño extra temporal).
- Reglas por arma o clase en archivos JSON independientes.
- Combos para NPCs usando los mismos componentes y eventos.

---

## Troubleshooting
- __No sube el combo__: revisa `rules.allowed_sources`, `min_damage`, `require_unique_target` y `same_target_cooldown_s`.
- __Se rompe solo__: puede estar expirando la ventana efectiva (mira `difficulty_increase_per_hit` y `min_window_s`) o llegarte eventos `break` por daño recibido.
- __No recarga la config__: asegúrate de editar `data/config/combo_rules.json` y de que cambie su mtime (guardar el archivo). El recargado sucede ~cada 1s.

---

## Orden de ejecución relevante
- Update (extracto):
  - `... HitboxSystem, ComboSystem, BuildingDamageSystem, ...`
- Render (extracto):
  - `... ExperienceRenderSystem, ComboBarRenderSystem, ...`

Esto garantiza que la barra de combo se dibuje por encima de la barra de experiencia y que la lógica de combo procese los eventos de impacto del frame.
