# Política de daño para Spawners Visuales

## Objetivo
- Unificar cómo los spawners visuales reciben daño del jugador, con precisión de colisión por forma (shape-first) y una política declarativa de fuentes (sources).
- Mantener compatibilidad: si no se define política, todo el daño del jugador se acepta como antes.

## Alcance
- Aplica a cualquier spawner con visual asociado (building vinculado vía `SpawnerVisualSync`, marcado con `_is_spawner_visual`).
- No afecta a edificios no-spawner (señalados por `BuildingDamageSystem`).

---

## Flujo de alto nivel
1) Un sistema de combate detecta colisión y publica un evento con metadatos:
   - Para spawner visual: `SpawnerDamageEvents` con `source`.
   - Colisión: por forma (máscara alfa de la imagen) y fallback a rectángulos si no hay máscara.
2) `SpawnerDamageSystem` consume `SpawnerDamageEvents` y decide, según la política `sources`, si aplica el daño.
3) Si procede, actualiza HP del spawner, dispara flash y opcionalmente HUD.

---

## Eventos y metadatos
- `SpawnerDamageEvents` (publicados por sistemas de combate):
  - `spawner_eid: int`
  - `damage: float`
  - `attacker: int | None` (entidad atacante)
  - `source: str` en {`"melee"`, `"fireball"`, `"puddle"`, `"mine"`} (extensible)

Fuentes actuales:
- **melee**: ataques de hitbox/arco/slash/cone (HitboxSystem)
- **fireball**: proyectiles de Fireball
- **puddle**: daño periódico en área (charcos)
- **mine**: explosiones de mina

---

## Colisión: forma (shape-first)
- Para spawners visuales, si el `BuildingModel` expone `get_full_mask()`:
  - Se prueba solape con máscara alfa del sprite.
  - Si NO hay máscara (p. ej., imagen no cargada), se cae a rectángulos por tiles.
- Beneficio: evita falsos positivos por bounding-box.

---

## Política de fuentes (`sources`)
- Declarativa y opcional. Si NO está definida, se acepta todo el daño del jugador.
- Ubicación (instancia de spawner):
  - Defaults a nivel de instancia: `overrides.life_defaults.sources`
  - Por estado visual: `visuals[STATE].life.sources`
- Formatos válidos:
  - Cadena: `"player"` (comodín: todas las fuentes del jugador)
  - Lista: `["melee", "fireball", "puddle", "mine"]`
- Normalización: se parsea a minúsculas.

Reglas:
- Si el atacante es el jugador:
  - Acepta daño si `sources` contiene `"player"` o contiene el `source` concreto del evento.
  - Si `sources` no existe: se acepta todo.
- Si el atacante NO es el jugador: `sources` no filtra (se aplica la lógica general de daño/neutralidad).

---

## Ejemplos de configuración (JSON)

Aceptar todo daño del jugador (comodín):
```json
"overrides": {
  "life_defaults": {
    "damageable": true,
    "max_hp": 1000,
    "sources": "player"
  }
}
```

Restringir a melee y fireball:
```json
"overrides": {
  "life_defaults": {
    "damageable": true,
    "max_hp": 1000,
    "sources": ["melee", "fireball"]
  }
}
```

Restringir por estado visual (solo fireball durante SpawningWave):
```json
"visuals": {
  "SpawningWave": {
    "instance_id": 157,
    "life": {
      "damageable": true,
      "sources": ["fireball"]
    }
  }
}
```

---

## Integración con sistemas
- Hitbox/Slash/Cone (HitboxSystem): publica `source: "melee"` y usa shape-first.
- Fireball: publica `source: "fireball"` y usa shape-first.
- Puddle: por tick publica `source: "puddle"`; shape-first.
- Mine: al detonar publica `source: "mine"`; shape-first.
- BuildingDamageSystem: ignora `_is_spawner_visual`; no interfiere con spawners.

---

## Edge cases y compatibilidad
- Sin máscara en building: se usa fallback de tiles.
- `visible_in_game=false`: el visual está oculto; no recibe golpes.
- Duplicados de building id: se avisa por log; el vínculo del spawner se hace a uno.
- Sin `sources` definido: comportamiento legacy (acepta todas las fuentes del jugador).

---

## Rendimiento
- Máscaras de building: cacheadas en `BuildingModel`.
- Puddle/Mine: crean máscara de círculo por tick/detonación (coste bajo). Si hubiese muchos AOE simultáneos, se puede cachear por diámetro.
- Culling preliminar por rectángulo antes de probar máscara.

---

## Cómo añadir una nueva fuente
1) En el sistema emisor, publica `SpawnerDamageEvents` con `source: "mi_fuente"`.
2) Documenta el tag en este listado.
3) Si necesitas filtrar por esa fuente, agrégala a `sources` en la instancia/estado del spawner.

---

## Checklist de adopción
- [ ] `damageable: true` en `life`.
- [ ] `visible_in_game: true` y visual correctamente vinculado.
- [ ] `sources` definido según la política deseada o omitido para permitir todas las fuentes del jugador.
- [ ] Probar melee y fireball en el borde de la silueta (shape-first).
- [ ] Probar puddle/mine dentro del área del visual.

---

## Preguntas frecuentes (FAQ)
- ¿Qué pasa si no defino `sources`?
  - Se aceptan todas las fuentes del jugador (compatibilidad hacia atrás).
- ¿Puedo habilitar solo daño a distancia?
  - Sí: `"sources": ["fireball", "puddle", "mine"]`.
- ¿Afecta a edificios normales?
  - No. Solo a spawners visuales (marcados con `_is_spawner_visual`).

---

## Glosario rápido
- **Shape-first (alpha mask)** — Colisión por píxel — Precisa en bordes — `mask.overlap()`.
- **Fallback** — Alternativa cuando no hay máscara — Rectángulos por tiles.
- **Fuente (source)** — Etiqueta del origen de daño — p.ej., `melee`, `fireball`.
- **Política declarativa** — Reglas en datos, no en código — Fácil tuning.
- **Compatibilidad hacia atrás** — Sin `sources`, todo funciona como antes.

---

## Notas de implementación
- Parser de `sources` en `config_resolver.py` (instancia y por estado visual) → normaliza a lista minúscula.
- Filtro en `SpawnerDamageSystem` → aplica solo si atacante es player y `sources` está presente.
- Sistemas emisores taggean `source` en `SpawnerDamageEvents`.

---

## Recomendación de nombre de archivo
- Alternativa más clara y sin acentos para portabilidad: `politica_dano_spawners.md` o, en inglés, `spawner_damage_policy.md`.
