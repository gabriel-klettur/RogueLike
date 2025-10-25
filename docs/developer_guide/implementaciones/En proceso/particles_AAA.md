
# Resumen
- Te propongo un esquema de parámetros “AAA” para partículas, organizado por categorías (emisión, inicialización, fuerzas, curvas, render, colisión, trails, sub-emisores, rendimiento).
- Incluye nombres de parámetros, tipo esperado, propósito y rangos recomendados.
- Trae un preset JSON de ejemplo que usa muchas de estas capacidades.
- Cierro con una hoja de ruta de adopción incremental para tu proyecto (qué soportar primero en tu preview/runtime actual).

# Plan
- Diseñar el esquema ampliado (compatible hacia atrás).
- Priorizar un subconjunto MVP para implementar en tu `preview_builder` y en el runtime.
- Definir ubicación de `JSON Schema` para validación.
- Crear presets demostrativos variados.

# Esquema de parámetros pro (nivel “AAA”)
Entre paréntesis incluyo el nombre en inglés cuando es término clave.

- Emisor (Emitter)
  - `emission_shape`: point | circle | ring | line | box | cone | mesh.
  - `emission_extent`: tamaño/medidas del shape. Ej: `[w, h]`, `radius`, `inner_radius`.
  - `emission_direction`: vector base. Ej: `[0, -1]`.
  - `emission_angle_spread_deg`: apertura angular.
  - `emit_rate`: partículas/segundo (o `bursts`).
  - `bursts`: lista de `{time_s, count, loop?}`.
  - `max_particles`: cota dura para el pool.
  - `simulation_space` (space): `"local"` | `"world"`.
  - `inherit_velocity`: factor [0..1] desde el emisor.

- Inicialización (Initializers)
  - `lifetime_s`: segundos, con `lifetime_jitter_s`.
  - `speed`: px/s con `speed_variance`.
  - `direction`: vector inicial o `angle_deg`.
  - `angular_velocity_deg_s`: giro por segundo.
  - `size_start`: `[w, h]` o escalar.
  - `aspect_ratio`: ancho/alto si usas escalar.
  - `color_start`: `[r,g,b]`.
  - `alpha_start`: 0..255.
  - `sprite`: id de textura/atlas.
  - `pivot`: `[0..1, 0..1]` centro de rotación.
  - `random_seed_mode`: `"per_particle"` | `"per_system"`.

- Fuerzas/Campos (Forces/Fields)
  - `gravity`: `[gx, gy]` o escalar (aplica en Y).
  - `drag`: 0..1 (amortiguación).
  - `wind`: `[wx, wy]`.
  - `radial_accel`: hacia/desde origen.
  - `tangential_accel`: giro alrededor del origen.
  - `attractors`: lista de `{pos:[x,y], strength, radius}`.
  - `turbulence_noise`: `{strength, frequency, octaves, seed}`.
  - `flow_field`: referencia a textura/campo vectorial opcional.

- Curvas sobre vida (Curves over life)
  - `size_over_life`: curva o pares `[t, value]` (t en 0..1).
  - `alpha_over_life`: curva 0..255.
  - `color_over_life`: gradiente `[[t, [r,g,b]], ...]`.
  - `speed_over_life`, `rotation_over_life`.
  - `stretch_by_velocity`: factor para estirar sprite según velocidad.

- Colisión/Interacción (Collision/Interaction)
  - `collide_with_world`: bool.
  - `collision_layers`: máscaras.
  - `collision_radius`: px.
  - `bounciness`: 0..1.
  - `friction`: 0..1.
  - `kill_on_collision`: bool.
  - `spawn_on_collision`: sub-emisor (preset id) y `probability`.

- Render/Material (Rendering/Material)
  - `blend_mode`: `"alpha"` | `"additive"` | `"premultiplied_alpha"`.
  - `layer`: entero o etiqueta para orden.
  - `z_bias`: flotante para sorting fino.
  - `soft_particles`: bool (si tienes depth).
  - `tint`: `[r,g,b]` global del sistema.
  - `hdr_bloom_hint`: bool (para pipeline que lo soporte).
  - `sprite_sheet`: `{atlas, frames, fps, loop, random_start}`.
  - `flip_x`, `flip_y`: bool.
  - `sorting`: `"by_depth"` | `"oldest_first"` | `"newest_first"`.

- Trails/Ribbons (Estelas)
  - `trail.enabled`: bool.
  - `trail.mode`: `"ribbon"` | `"per_particle"` (poli-linea).
  - `trail.max_points`: entero.
  - `trail.min_distance`: px entre puntos.
  - `trail.width_over_life`: curva.
  - `trail.alpha_over_life`: curva.
  - `trail.color_over_life`: gradiente.
  - `trail.texture`: id y `uv_mode: "stretch"|"tile"`.

- Sub-emisores/Eventos (Sub-emitters/Events)
  - `on_birth`, `on_death`, `on_collision`, `on_interval_s`.
  - Cada evento: `{preset_id, amount, probability, offset:[x,y]}`.

- Rendimiento/LOD (Performance/LOD)
  - `fixed_dt`: ms (simulación estable).
  - `pool_size`: tamaño de pool prealocado.
  - `spawn_budget_per_frame`: cota de spawns/frame.
  - `bounds`: AABB para culling.
  - `lod_rules`: lista de `{distance, emit_multiplier, skip_sim?}`.
  - `sleep_when_offscreen`: bool.

- Depuración (Debug)
  - `debug_draw_emit_shape`: bool.
  - `debug_color_age`: bool.
  - `debug_freeze`: bool.

# Ejemplo JSON de preset avanzado
Este ejemplo es deliberadamente “rico”; tu preview actual ignorará lo que no entiende, pero sirve como contrato para extender el runtime/editor.

```json
{
  "id": "storm_vortex_with_sparks",
  "name": "Storm Vortex with Sparks",
  "type": "fx",
  "vfx": {
    "preview": "particles",
    "particles": {
      "kind": "smoke_emitter",
      "emission_shape": "ring",
      "emission_extent": { "radius": 28, "inner_radius": 16 },
      "emission_direction": [0, -1],
      "emission_angle_spread_deg": 360,
      "emit_rate": 120,
      "bursts": [{ "time_s": 0.0, "count": 40 }],
      "max_particles": 800,
      "simulation_space": "world",
      "inherit_velocity": 0.2,

      "lifetime_s": 1.8,
      "lifetime_jitter_s": 0.4,
      "speed": 140,
      "speed_variance": 60,
      "angular_velocity_deg_s": 90,
      "size_start": [4, 4],
      "color_start": [180, 200, 255],
      "alpha_start": 220,
      "sprite": "fx/smoke_puff",

      "gravity": [0, 60],
      "drag": 0.18,
      "turbulence_noise": { "strength": 80, "frequency": 1.6, "octaves": 3, "seed": 7 },
      "radial_accel": -120,
      "tangential_accel": 90,

      "size_over_life": [[0.0, 0.6], [0.5, 1.3], [1.0, 0.2]],
      "alpha_over_life": [[0.0, 0], [0.1, 220], [0.8, 160], [1.0, 0]],
      "color_over_life": [[0.0, [140, 180, 255]], [0.6, [200, 220, 255]], [1.0, [180, 200, 255]]],
      "speed_over_life": [[0.0, 1.0], [1.0, 0.4]],
      "stretch_by_velocity": 0.5,

      "collide_with_world": true,
      "collision_radius": 2,
      "bounciness": 0.2,
      "friction": 0.1,
      "kill_on_collision": false,
      "spawn_on_collision": { "preset_id": "spark_burst_small", "amount": 1, "probability": 0.5 },

      "blend_mode": "additive",
      "layer": "fx_foreground",
      "z_bias": 0.01,
      "tint": [180, 200, 255],
      "sprite_sheet": { "atlas": "fx/atlas.png", "frames": [0, 1, 2, 3], "fps": 12, "loop": true },

      "trail": {
        "enabled": true,
        "mode": "ribbon",
        "max_points": 8,
        "min_distance": 3,
        "width_over_life": [[0.0, 4], [1.0, 0]],
        "alpha_over_life": [[0.0, 200], [1.0, 0]],
        "color_over_life": [[0.0, [200, 220, 255]], [1.0, [140, 160, 200]]],
        "texture": "fx/ribbon_soft",
        "uv_mode": "stretch"
      },

      "on_death": { "preset_id": "mist_fade", "amount": 2, "probability": 0.7 },

      "fixed_dt": 16,
      "pool_size": 1000,
      "spawn_budget_per_frame": 200,
      "bounds": { "x": -256, "y": -256, "w": 512, "h": 512 },
      "lod_rules": [{ "distance": 600, "emit_multiplier": 0.5 }],
      "sleep_when_offscreen": true,

      "debug_draw_emit_shape": false
    }
  }
}
```

Sugerencia de ubicación para el esquema: `schemas/editors/particles/particles.schema.json` (en línea con tu árbol actual).

# Adopción incremental recomendada (tu proyecto)
- Fase 1 (rápida, visible en el editor/preview)
  - Soportar: `emission_shape`, `drag`, `gravity`, `blend_mode`, `size_over_life`, `alpha_over_life`, `color_over_life`.
  - Resultado: ya puedes crear lluvia/neblina/niebla, chispas que se desvanecen y flamas con crecimiento/decadencia natural.
- Fase 2
  - `trails`, `sub-emitters` (on_death/on_collision), `bursts`, `inherit_velocity`, `angular_velocity`.
- Fase 3
  - Colisión básica con AABB del mundo, `bounciness`/`friction`, `spawn_on_collision`.
- Fase 4
  - Turbulencia/ruido, `flow_field`, LOD y presupuestos de spawn.

Si quieres, te preparo un `JSON Schema` inicial y extiendo tu `preview_builder` con el subconjunto de Fase 1.

# Riesgos y buenas prácticas
- Rendimiento (Performance): prealocar `pool_size`, usar `fixed_dt` y limitar `spawn_budget_per_frame`.
- Pygame blending: “additive” se puede simular con superficies y `BLEND_ADD`; diseño preparado para futuras migraciones a una pipeline más rica.
- Consistencia de unidades: segundos para tiempos (`_s`), píxeles por segundo para velocidades, 0..1 para curvas de vida.

# Glosario rápido
- **Emitter (emisor)** — Origen y cadencia de partículas — Define cómo/cuándo nacer — Ej: `emit_rate: 60`, `emission_shape: "cone"`.
- **Simulation space (espacio de simulación)** — Sistema local vs mundo — Útil para adjuntar a entidades — Ej: `"local"`.
- **Drag (amortiguación)** — Reduce velocidad cada frame — Suaviza movimiento — Ej: `drag: 0.2`.
- **Gradient (gradiente)** — Colores que cambian en el tiempo — Para transiciones — Ej: `color_over_life`.
- **Curve over life (curva de vida)** — Mapea vida [0..1] a un valor — Control fino de tamaño/alpha — Ej: `size_over_life`.
- **Sub-emitter (sub-emisor)** — Emisor disparado por evento — Composición de efectos — Ej: `on_death`.
- **Trail/Ribbon (estela/cinta)** — Historia visual de movimiento — Añade sensación de velocidad — Ej: `trail.enabled: true`.
- **LOD (nivel de detalle)** — Cambia calidad por distancia — Mantiene rendimiento — Ej: `lod_rules`.

## Subconjunto AAA implementado (preview/runtime actual)

- Emisor (preview y runtime según tipo):
  - `emission_shape`: `point|circle|ring|box|line|cone` (aura soporta todos; línea y cono añadidos).
  - `emission_extent`: número o array (radio, `[inner,outer]`, `[w,h]`, o fracción ≤1.0).
  - `emission_direction` y `emission_angle_spread_deg` (dirección base + apertura).
  - `emit_rate` y `bursts` (preview y runtime HealingAura; `loop` soportado).
  - `simulation_space`: `local|world` (Dash/Laser/Slash/HealingAura en runtime). `local` ancla con `anchor_eid`.
  - `max_particles`: cota por emisor cuando `simulation_space == "local"` (Dash/Laser/Slash/HealingAura en runtime).
  - `inherit_velocity`: factor [0..1] (Dash/HealingAura en runtime). 

- Inicialización:
  - `speed_variance`, `lifetime_jitter` (ratio <1 o frames), `size_start` (escalar o `[w,h]`). 

- Curvas y render:
  - `size_over_life`, `alpha_over_life`, `color_over_life` (preview y runtime genérico).
  - `blend_mode`: `alpha|additive` (preview y runtime; additive con `BLEND_ADD`). 

- Validación (preview):
  - Avisos (logger.warning) si curvas fuera de [0,1] o desordenadas, shapes desconocidas o extents inválidos.

### Ejemplos rápidos

Aura con línea y bursts (preview y runtime):

```json
{
  "kind": "aura",
  "emit_rate": 3,
  "speed": 1.0,
  "lifespan": 60,
  "size_range": [4, 8],
  "colors": [[255,240,200],[200,230,255]],
  "blend_mode": "additive",
  "emission_shape": "line",
  "emission_extent": 0.6,
  "emission_direction": [0,-1],
  "emission_angle_spread_deg": 20,
  "bursts": [{"time_s": 0.0, "count": 8}, {"time_s": 0.2, "count": 12, "loop": true}],
  "simulation_space": "local",
  "max_particles": 48,
  "inherit_velocity": 0.4,
  "size_over_life": [[0,0.8],[0.5,1.2],[1,0.7]],
  "alpha_over_life": [[0,0],[0.1,1],[0.8,0.6],[1,0]],
  "color_over_life": [[0,[255,255,220]],[1,[180,255,220]]]
}
```

Aura con cono (sector) orientado por `emission_direction`:

```json
{
  "kind": "aura",
  "emission_shape": "cone",
  "emission_extent": 16,
  "emission_direction": [1, 0],
  "emission_angle_spread_deg": 30,
  "speed": 1.0,
  "emit_rate": 3
}
```

Dash/Laser con espacio local y límite:

```json
{
  "particle_simulation_space": "local",
  "max_particles": 64,
  "particle_blend_mode": "additive",
  "particle_alpha_over_life": [[0,1],[1,0]]
}
```

### Notas de compatibilidad

- Todos los campos nuevos son opcionales. Si no están presentes, el comportamiento previo se mantiene.
- `lifetime_jitter`: valores <1 son proporcionales a la vida; ≥1 se interpretan en frames (preview) o frames aproximados (runtime).
- En `line` sin `emission_extent`, se usa el ancho del óvalo/caja por defecto.

### Tabla de soporte rápida (Preview/Runtime)

| Parámetro | Preview | HealingAura (RT) | Dash (RT) | Slash (RT) | Laser (RT) |
|---|---:|---:|---:|---:|---:|
| emission_shape (point/circle/ring/box) | ✔️ | ✔️ | – | – | – |
| emission_shape (line) | ✔️ | ✔️ | – | – | – |
| emission_shape (cone) | ✔️ | ✔️ | – | – | – |
| emission_extent | ✔️ | ✔️ | – | – | – |
| emission_direction | ✔️ | ✔️ | – | – | – |
| emission_angle_spread_deg | ✔️ | ✔️ | – | – | – |
| bursts (loop) | ✔️ | ✔️ | – | – | – |
| blend_mode (additive/alpha) | ✔️ | – | ✔️ | ✔️ | ✔️ |
| size_over_life | ✔️ | – | ✔️ | ✔️ | ✔️ |
| alpha_over_life | ✔️ | – | ✔️ | ✔️ | ✔️ |
| color_over_life | ✔️ | – | ✔️ | ✔️ | ✔️ |
| drag / gravity | – | – | ✔️ | ✔️ | ✔️ |
| speed_variance | ✔️ | ✔️ | – | – | – |
| lifetime_jitter | ✔️ | ✔️ | – | – | – |
| size_start | ✔️ | ✔️ | – | – | – |
| simulation_space (local/world) | – | ✔️ | ✔️ | ✔️ | ✔️ |
| max_particles (cap) | – | ✔️ | ✔️ | ✔️ | ✔️ |
| inherit_velocity | – | ✔️ | ✔️ | – | – |

Notas:
- "RT" = runtime. Un guion (–) indica “no aplica” o “no implementado” para ese emisor.
- HealingAura implementa el set de emisión (shape/direction/extent/spread/bursts) y parte de inicialización.
- Dash/Slash/Laser se enfocan en material/curvas/fuerzas y control de simulación (anchor/caps).

## Texturizado y Flipbook (preview/runtime)

- Permite dibujar partículas usando una textura o sprite sheet con animación por frames.
- Campos nuevos (opcionales) en `ParticleComponent`/schema:
  - `texture_path`: ruta del archivo de imagen (PNG con alfa recomendado).
  - `flipbook`: objeto con `cols`, `rows`, `total`, `frame_w`, `frame_h`, `loop`.

Ejemplo mínimo (Smoke/HealingAura en Preview y runtime):

```json
{
  "particle_blend_mode": "additive",
  "particle_alpha_over_life": [[0, 1], [1, 0]],
  "texture_path": "assets/particles/spark_sheet.png",
  "flipbook": { "cols": 4, "rows": 4, "total": 16, "frame_w": 16, "frame_h": 16, "loop": true }
}
```

Notas:
- El Preview soporta `texture_path` y `flipbook` en Smoke y HealingAura.
- Se recomienda atlas con celdas cuadradas y, si es posible, alfa premultiplicada.
- Mantener tamaños pequeños (16–64px) y agrupar materiales para rendimiento.
