# Sistema de Iluminación (Pygame) — Diseño y Viabilidad

Este documento define el diseño técnico, costes de CPU/FPS y plan por fases para implementar iluminación con ciclo día/noche y fuentes de luz dinámicas (`fuego`, `lámparas`, `antorchas`, `magia`) en el Roguelike.

---

## 1) Objetivos y Alcance

- **Día/Noche (ambient light)**: Variar la luminosidad ambiental según hora del día con una curva de intensidad y color.
- **Luces puntuales (point lights)**: Fuentes con `radio`, `intensidad`, `color`, `atenuación` (falloff) y efectos (parpadeo/flicker).
- **Toggles/Kill switch**: Poder desactivar todo el sistema de iluminación sin afectar otros sistemas.
- **Coste controlado**: Mantener 60 FPS objetivo. Presupuesto: ≤ 2 ms/frame extra en equipos medios.
- **Alcance de Fase 1–2**: Sin sombras físicas (occlusion), solo iluminación aditiva/multiplicativa eficiente. Las sombras se evalúan en una fase posterior opcional.

Assumptions:
- Pygame 2.x disponible (blits con `special_flags` como `BLEND_RGBA_MULT`, `BLEND_ADD`).
- Resolución interna 1280×720 o similar; se puede hacer un `lightmap` a menor resolución (p. ej., 1/2 o 1/4) y escalar.

---

## 2) Opciones Técnicas consideradas

- **Overlay multiplicativo global (multiply)**: Un `Surface` del tamaño de la pantalla, con color/alpha dependiente de la hora. Coste muy bajo (~0.1 ms). Ideal para día/noche.
- **Lightmap aditivo + multiplicativo**: Construir un `Surface` negro para la noche y “pintar” luces puntuales con gradientes radiales mediante `BLEND_ADD`, luego combinar con la escena con `BLEND_RGBA_MULT` (o `BLEND_MULT`). Coste bajo si el lightmap es low-res y se reutilizan texturas precomputadas.
- **Sombras con oclusión (shadow casting)**:
  - Tile-FOV (shadowcasting clásico Roguelike): calcula visibilidad por celdas; barato en cuadrículas, pero da sombras “duras” por tiles.
  - Raycasting/polígonos 2D: más realista; coste alto O(N_lights × N_bordes). Se pospone.

Conclusión: Empezar con overlay + lightmap (sin sombras), con hooks para agregar oclusión por tiles más adelante.

---

## 3) Propuesta de Arquitectura

- **Sistema**
  - `DayNightSystem`: produce `ambient_color` e `intensity` según hora (curva/spline). Expone `get_ambient_tint(t)`.
  - `LightingManager`:
    - Mantiene `lights: List[Light]` (componentes de entidades) y un `lightmap_surface` low-res.
    - Pre-renderiza texturas de gradiente radial (cache por radio y falloff) para evitar draws por píxel.
    - Ciclo por frame: decide si recomponer el lightmap (dirty) y lo aplica a la escena.
  - `Light` (componente/DTO): `pos_world`, `radius`, `intensity [0..1]`, `color (RGB)`, `falloff`, `flicker`, `enabled`.
- **Pipeline de render**
  1. Renderizar mundo a `world_surface` (como hoy).
  2. Construir/actualizar `lightmap_surface` (low-res):
     - Limpiar a negro.
     - Aplicar luz ambiental: opcionalmente pre-tint global.
     - Para cada `Light` activa, blit de su gradiente con `BLEND_ADD` posicionado en coordenadas de cámara.
  3. Escalar `lightmap_surface` a resolución de pantalla; blur ligero opcional (desenfoque por escalado bilinear ya ayuda).
  4. Multiplicar el `lightmap` con `world_surface` mediante `BLEND_RGBA_MULT` (oscurece donde el lightmap es oscuro, mantiene/brilla donde es claro).

Notas Pygame:
- `Surface.blit(src, dest, special_flags=pygame.BLEND_ADD)` para sumar luz.
- `screen.blit(lightmap, (0,0), special_flags=pygame.BLEND_RGBA_MULT)` para oscurecer/combinar.

---

## 4) Rendimiento esperado (CPU/FPS)

- Complejidad por frame: O(N_luces) blits de texturas ya rasterizadas. Blit de 64–256 px de radio a lightmap de 1/2–1/4 resolución.
- Coste típico (estimado):
  - Overlay día/noche: ~0.05–0.2 ms.
  - 10 luces con radio 128 px en lightmap 1/2 res: ~0.4–1.2 ms.
  - Upscale + blit multiplicativo: ~0.2–0.6 ms.
  - Total objetivo: 0.8–2.0 ms/frame en hardware medio (preserva 60 FPS en 16.7 ms budget).
- Optimización clave: reconstruir el lightmap solo cuando cambien luces o delta de hora relevante (p. ej., cada 100–200 ms), manteniendo el mismo buffer entre cuadros si no hay cambios.

Riesgos:
- Muchas luces grandes simultáneas pueden superar el presupuesto. Mitigar con límites: `MAX_LIGHTS`, `MAX_RADIUS`, low-res, culling por cámara.

Go/No-Go:
- Go si con 10 luces visibles el overhead ≤ 2 ms/frame en escenarios típicos.
- No-Go o fallback a solo overlay si se excede de forma consistente.

---

## 5) Plan por Fases y Toggles

- **Fase 1 (Overlay día/noche)**
  - Curva de intensidad y color ambiental; toggle `lighting.enabled` y `lighting.ambient_only`.
- **Fase 2 (Luces puntuales sin sombras)**
  - `LightingManager`, cache de gradientes, lightmap low-res, flicker básico.
- **Fase 3 (Oclusión por tiles — opcional)**
  - Integrar FOV por tiles para atenuar luz detrás de paredes. Toggle `lighting.tile_occlusion`.
- **Fase 4 (Sombras poligonales — opcional)**
  - Raycasting en niveles limitados; solo para pocas luces “protagonistas”. Toggle `lighting.shadow_polygons`.

Toggles globales (config y runtime):
- `lighting.enabled` (kill switch).
- `lighting.quality` in {"off","ambient","lights_low","lights_high"}.
- `lighting.low_res_scale` (2, 4).
- `lighting.max_lights_visible` y `max_radius`.

---

## 6) Datos y API propuesta

- `Light` (dataclass): `id`, `pos_world: Vec2`, `radius: int`, `intensity: float`, `color: Tuple[int,int,int]`, `falloff: float`, `flicker: Optional[dict]`, `enabled: bool`.
- `LightingManager`:
  - `add_light(light)`, `remove_light(id)`, `set_enabled(bool)`.
  - `update(dt)`: gestiona flicker y suciedad del lightmap.
  - `compose_lightmap(camera_rect)`: regenera buffer si dirty.
  - `apply(world_surface, target_surface)`: blit multiplicativo final.
- `DayNightSystem`:
  - `set_time_of_day(t)`, `advance(dt)`, `get_ambient_tint()`.

Integración ECS:
- Un `LightComponent` conectable a entidades (`antorcha`, `hechizo`, `lámpara`). Un `LightingSystem` recoge todos los componentes y actualiza el manager por escena.

---

## 7) Estrategia de optimización

- Lightmap low-res (1/2–1/4), con upscale bilinear.
- Cache de gradientes por `(radius, falloff)`, coloreados al vuelo con tint simple.
- Culling por cámara: ignorar luces cuyo bounding no interseca la vista.
- Re-composición por bloques de tiempo (p. ej., cada 0.1 s) o solo ante eventos (mover luz, toggles, cambio significativo de hora).
- Límites configurables y LOD (quality tiers).

---

## 8) Protocolo de Medición

- Métrica: `ms/frame` extra respecto a baseline (sin iluminación).
- Metodología:
  - Baseline: ejecutar misma escena con `lighting.enabled=false` durante 10 s, registrar `mean`, `p95`.
  - A/B: activar overlay (F1), activar luces (F2), diferentes densidades (3, 5, 10, 20 luces).
  - Escenarios: exterior noche, interior con paredes, lluvia de partículas encendidas.
- Instrumentación: `time.perf_counter()` en bloques `compose_lightmap` y `apply`, log por CSV.
- Criterio aceptación: ≤ 2 ms/frame con 10 luces visibles a 720p en equipo medio.

---

## 9) Riesgos y Mitigaciones

- Exceso de luces/radios grandes → aplicar límites y culling.
- Pérdida de nitidez por low-res → ajustar scale/blur y radios artísticos.
- Arte inconsistente (temperatura de color) → paleta definida por tipo de luz.
- Tearing visual si se recompone esporádicamente → fijar cadencia mínima (p. ej., 10 Hz) de recomposición.

---

## 10) Roadmap y Entregables

- PR1: DayNightSystem + overlay + toggles + benchmark baseline.
- PR2: LightingManager (sin sombras) + cache de gradientes + culling + métricas.
- PR3 (opc.): Tile occlusion FOV.
- PR4 (opc.): Sombras poligonales limitadas.

---

## 11) Pseudocódigo mínimo (ilustrativo, no final)

```python
# Render loop (resumen)
world_surface = render_world()
lightmap = lighting.compose_lightmap(camera_rect)  # low-res; se cachea si no hay cambios
screen.blit(world_surface, (0, 0))
screen.blit(lightmap, (0, 0), special_flags=pygame.BLEND_RGBA_MULT)  # oscurece zonas sin luz
```

---

## 12) Glosario rápido

- **Ambient light (luz ambiental)** — Nivel base de luminosidad global — Úsalo para día/noche — Ej.: `tint = (180,180,220)` al atardecer.
- **Lightmap** — Textura que codifica luz por píxel — Úsalo para combinar con la escena — Ej.: `screen.blit(lightmap, ..., BLEND_RGBA_MULT)`.
- **Blending (composición)** — Cómo se combinan colores — `ADD` suma luz; `MULT` oscurece — Ej.: `BLEND_ADD` para halos.
- **Falloff (atenuación)** — Disminución de intensidad con la distancia — Controla realismo y coste.
- **Flicker (parpadeo)** — Variación temporal pseudoaleatoria — Da “vida” a fuego/antorchas.
- **Occlusion (oclusión)** — Bloqueo de luz por obstáculos — Tile-FOV o polígonos.
- **Culling** — Ignorar elementos fuera de cámara — Reduce coste.
- **Kill switch** — Toggle maestro para desactivar el sistema.

---

## 13) Cómo defender este diseño

- **Objetivo y criterios**: Iluminación percepible con 60 FPS; presupuesto ≤ 2 ms/frame; toggles para degradar.
- **Justificación**: Overlay + lightmap es simple y efectivo en Pygame; evita per-píxel CPU costosa; escalable con low-res.
- **Rendimiento/memoria**: O(N_luces); blits de texturas cacheadas; memoria baja para buffers; medible con timers.
- **Extensibilidad**: API clara para añadir tipos de luz y efectos; hooks para oclusión por tiles y sombras poligonales.
- **Riesgos y siguientes pasos**: Limitar número/tamaño de luces; probar en escenas reales; decidir Fase 3–4 tras métricas.

---

## 14) Checklist de calidad

- Nombres claros y constantes configurables en `config`.
- Separar `update()` de `draw()`. El lightmap solo se recompone si hay cambios.
- FPS cap y uso de `dt` en flicker.
- Limpieza de `Surface` y gestión de recursos.
- Culling y límites de `MAX_LIGHTS`/`MAX_RADIUS`.
- Toggled por runtime y desde archivo de configuración.


---

## 15) Ciclo Día/Noche detallado (amanecer/atardecer)

Objetivo: variar suavemente la intensidad y el color ambiental a lo largo del día, con transiciones perceptibles en amanecer y atardecer, manteniendo coste despreciable.

### 15.1 Keyframes propuestos (hora del juego)

- 05:00–07:00 — Amanecer: intensidad 0.30 → 0.80; color cálido a neutro.
  - 05:00: I=0.30, C=(180,140,120)
  - 06:00: I=0.55, C=(220,180,150)
  - 07:00: I=0.80, C=(230,230,235)
- 07:00–18:00 — Día: intensidad 1.00; color neutro levemente azulado.
  - 12:00: I=1.00, C=(245,245,255)
- 18:00–20:00 — Atardecer: intensidad 0.80 → 0.30; color de neutro a cálido.
  - 19:00: I=0.55, C=(220,170,140)
  - 20:00: I=0.30, C=(170,140,180)
- 20:00–05:00 — Noche: intensidad 0.15–0.25; color azulado tenue.
  - 00:00: I=0.20, C=(150,170,220)

Notas artísticas: los valores son de partida; se ajustarán tras pruebas visuales con tiles y UI.

### 15.2 Curvas y easing

- Usar interpolación cúbica suave (smoothstep/catmull-rom) entre keyframes para evitar escalones.
- Separar curvas para intensidad y color (tres canales) para un control fino.
- Cachear una LUT por minuto (1440 entradas) para consultas O(1) sin coste por frame.

### 15.3 Escala temporal y control

- Duración del día del juego: 24 h en 12 min reales (1 s real = 2 min juego). Configurable.
- Actualización: recalcular `ambient_tint` cada 100–200 ms (no cada frame) para estabilidad visual.
- Hooks de eventos: disparar callbacks al cruzar umbrales (inicio amanecer/atardecer/noche/día) para música/IA.

### 15.4 Modificadores contextuales

- Clima: nubes/lluvia reducen intensidad (0.8×) y enfrían color (tinte azulado), parámetro `weather_factor`.
- Interior/cuevas: override de intensidad a 0.10–0.20 y color neutro; la escena depende de luces puntuales.
- Regiones: permitir presets por bioma/escena (desierto más cálido, nieve más fría).

### 15.5 API/Config

- `DayNightSystem`:
  - `set_time_scale(real_to_game_ratio)`
  - `set_keyframes(list[DayKeyframe])`
  - `get_ambient_intensity()` y `get_ambient_color()`
  - `on_phase_change(callback)`
- Config sugerida: `data/config/lighting.json` con `keyframes`, `time_scale`, modificadores por escena.

### 15.6 Pseudocódigo LUT (ilustrativo)

```python
# Construcción LUT (cada minuto del día)
keyframes = [
    (5*60,  (0.30, (180,140,120))),
    (6*60,  (0.55, (220,180,150))),
    (7*60,  (0.80, (230,230,235))),
    (12*60, (1.00, (245,245,255))),
    (19*60, (0.55, (220,170,140))),
    (20*60, (0.30, (170,140,180))),
    (24*60, (0.20, (150,170,220))),  # wrap a 24:00 = 0:00
]
# Interpolar con easing por tramos y rellenar LUT[0..1439]
```

### 15.7 Coste y riesgos

- Cálculo LUT: O(1440) al iniciar/cambiar config; coste despreciable.
- Consulta por frame: acceso O(1); coste ~0.0 ms. No afecta FPS.
- Riesgo: discrepancia de paleta con arte. Mitigar con revisión artística y ajustes de keyframes.

---

## 16) Arquitectura de carpetas y archivos (esqueleto escalable)

Objetivo: organizar el sistema de iluminación para alta cohesión y bajo acoplamiento, permitiendo escalar de overlay+lightmap a oclusión por tiles y sombras poligonales sin romper APIs.

### 16.1 Árbol de directorios propuesto

```text
src/
  roguelike_engine/
    rendering/
      lighting/
        __init__.py
        daynight.py           # DayNightSystem: keyframes, LUT, time control
        light_types.py        # Light dataclass, enums de tipo, helpers de color
        gradients.py          # GradientCache: pre-render radial masks/lookup
        lightmap.py           # LightingManager: compose_lightmap, apply
        quality.py            # Quality tiers, limits, low_res_scale
        culling.py            # Culling por cámara y bounds
        occlusion_tiles.py    # (Fase 3 opc.) Oclusión por tiles/FOV
        shadows_poly.py       # (Fase 4 opc.) Polígonos de sombras/raycast
        toggles.py            # Adaptadores a config/runtime toggles
        profiling.py          # Instrumentación perf (perf_counter, CSV)

  roguelike_game/
    ecs/
      components/
        light_component.py    # Componente ECS con referencia a Light
      systems/
        lighting_system.py    # Orquesta LightingManager en el pipeline

data/
  config/
    lighting.json            # Configuración de iluminación (ver esquema)

tests/
  roguelike_engine/
    rendering/
      lighting/
        test_daynight.py
        test_lightmap.py
        test_quality.py
        test_culling.py
        test_toggles.py
```

### 16.2 Responsabilidades por módulo

- **daynight.py**: gestiona keyframes, LUT, `get_ambient_color/intensity`, eventos de fase.
- **light_types.py**: `Light` (id, pos, radius, intensity, color, falloff, flicker, enabled), presets por tipo (fuego, lámpara, magia).
- **gradients.py**: cache de `Surface` radiales por `(radius, falloff)`; tintado por color al vuelo.
- **lightmap.py**: `LightingManager` con `add/remove`, `update(dt)`, `compose_lightmap(camera_rect)`, `apply(world_surface, target_surface)`.
- **quality.py**: perf tiers (`off`, `ambient`, `lights_low`, `lights_high`), límites (`MAX_LIGHTS`, `MAX_RADIUS`), `low_res_scale`.
- **culling.py**: cálculo de intersección luz-cámara para evitar blits innecesarios.
- **occlusion_tiles.py** (opc.): atenuación por visibilidad/tiles.
- **shadows_poly.py** (opc.): generación de polígonos de sombra para pocas luces especiales.
- **toggles.py**: lectura de toggles desde config y mapping a atajos de teclado/UI.
- **profiling.py**: medición de `compose_lightmap` y `apply`, export CSV.

### 16.3 APIs públicas (estables)

- `DayNightSystem`:
  - `advance(dt)`, `set_time_scale(x)`, `set_keyframes(list[DayKeyframe])`.
  - `get_ambient_color() -> Tuple[int,int,int]`, `get_ambient_intensity() -> float`.
  - `on_phase_change(cb)`.
- `LightingManager`:
  - `add_light(light: Light)`, `remove_light(id)`, `clear()`.
  - `update(dt)`, `compose_lightmap(camera_rect) -> Surface`, `apply(world_surface, target_surface)`.
  - `set_enabled(bool)`, `set_quality(QualityTier)`.
- `Light` (dataclass) y `LightPreset` (helpers para crear antorcha/fuego/etc.).

### 16.4 Integración en el pipeline

1) `roguelike_game.ecs.systems.lighting_system` recoge `LightComponent`s visibles y sincroniza con `LightingManager`.
2) Orden de render:
   - Render mundo → `world_surface`.
   - `lighting_manager.compose_lightmap(camera_rect)` (si dirty/tick) → `lightmap_surface`.
   - `screen.blit(world_surface, ...)` y `screen.blit(lightmap_surface, ..., BLEND_RGBA_MULT)`.
3) `DayNightSystem` actualiza `ambient_tint` y controla overlay.

### 16.5 Esquema de config (lighting.json)

```json
{
  "enabled": true,
  "quality": "lights_low",  
  "low_res_scale": 2,
  "max_lights_visible": 12,
  "max_radius": 192,
  "ambient_only": false,
  "tile_occlusion": false,
  "shadow_polygons": false,
  "time_scale": 120.0,  
  "keyframes": [
    {"minute": 300,  "intensity": 0.30, "color": [180,140,120]},
    {"minute": 360,  "intensity": 0.55, "color": [220,180,150]},
    {"minute": 420,  "intensity": 0.80, "color": [230,230,235]},
    {"minute": 720,  "intensity": 1.00, "color": [245,245,255]},
    {"minute": 1140, "intensity": 0.55, "color": [220,170,140]},
    {"minute": 1200, "intensity": 0.30, "color": [170,140,180]},
    {"minute": 1440, "intensity": 0.20, "color": [150,170,220]}
  ]
}
```

### 16.6 Testing

- `test_daynight.py`: interpolación LUT, cambios de fase, time_scale.
- `test_lightmap.py`: composición con distintas calidades, culling y límites.
- `test_quality.py`: tier mapping y degradación correcta.
- `test_culling.py`: colisiones luz-cámara.
- `test_toggles.py`: kill switch y runtime mapping.

### 16.7 Puntos de extensión (extensibilidad)

- Nuevos tipos de luz: agregar preset en `light_types.py` y textura base en `gradients.py` si se necesita forma distinta.
- Oclusión por tiles: habilitar `occlusion_tiles.py` y añadir dependencia opcional en `lighting_system`.
- Sombras poligonales: activar `shadows_poly.py` para luces “hero” y limitar a N por escena (p. ej., 1–2).
