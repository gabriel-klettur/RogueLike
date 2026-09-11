# Auditoría de belleza del HUD de personaje (retrato · vida · maná · XP · slots)

**Fecha:** 2026-09-11 · **Nota global: 3.4 / 10** · Objetivo: **≥ 8.5** ·
**Tras la reconstrucción del mismo día: 8.9 / 10** (sección 7)

Alcance: el panel unificado inferior-izquierdo que construye `HUDManager.CreatePlayerHUD`
(`UI/HUD/HUDManager.cs`, `.PlayerPanel.cs`, `.XpBar.cs`) y sus drivers `PlayerHUD`,
`XpBarHUD`, `LevelLabelHUD`, `PlayerAbilityRowHUD`, `CooldownRing`. Fuera de alcance: minimapa,
reloj, combo, stance chip, boss bar (solo se citan por coherencia).

Método: lectura completa del código que lo dibuja + la captura de referencia adjunta (dwarf,
Lvl 0, 70/200, 34/35, tres slots). Cada eje va con la evidencia en código, no con gusto.

---

## 1. Puntuación por eje

| # | Eje | Nota | Evidencia |
|---|---|---|---|
| 1 | Composición / layout | 5 | Retrato + pila vertical es la estructura correcta y estable (`HorizontalLayoutGroup` + `VerticalLayoutGroup`). Pero todo son rectángulos del mismo peso a 4 px de separación: nada manda, nada respira |
| 2 | Jerarquía visual | 3 | Vida (22 px), maná (22 px) y XP (18 px) tienen casi la misma altura y el mismo tratamiento. La vida —la única barra que decide si sigues jugando— no es más grande, más brillante ni más ornamentada que el maná |
| 3 | Materialidad del panel | 2 | `panelImg.color = (0,0,0,0.55)` sobre un `Image` sin sprite. Es un rectángulo negro translúcido: sin marco, sin borde, sin chaflán, sin textura. Lee como placeholder de prototipo |
| 4 | Materialidad de las barras | 2 | Fondo y relleno son `GetWhitePixelSprite()` (textura 4x4 estirada) en `Sliced`/`Filled`. Sin borde, sin highlight en el filo, sin marcas de cuarto, sin esquinas. Es exactamente lo que las barras del mundo tenían ANTES de su rebuild (ver CLAUDE.md, "Las barras sobre la cabeza") |
| 5 | Paleta / color | 4 | Verde puro (0.20,0.85,0.20), azul (0.31,0.47,1.0), amarillo (1.0,0.82,0.20) saturados sobre negro. Legibles pero "de programador": ningún tono comparte temperatura con el `UITheme.ACCENT` dorado que ya usan los 17 editores ni con la paleta del mundo |
| 6 | Tipografía | 3 | TMP por defecto, bold 13 px, blanco al 95 %, centrado sobre el relleno. Sin outline ni sombra: sobre el verde claro el "70/200" pierde contraste. `Lvl 0` a 12 px bold. Sin fuente propia del juego |
| 7 | Retrato | 3 | `ResolvePlayerPortraitSprite` toma el PRIMER `SpriteRenderer.sprite` del jugador: es el cuerpo entero en pose idle, no una cara. En 108 px un dwarf de cuerpo entero es un muñeco de 60 px de alto rodeado de vacío. Sin marco, sin fondo, sin viñeta |
| 8 | Badge de nivel | 2 | Pill oscura con "Lvl N". Funcional. **Muestra `Lvl 0` en la captura** — ver defecto D1 |
| 9 | Barra de vida | 4 | Lerp suave (`smoothSpeed 8`) y cambio a rojo bajo el 25 %. Es todo. No distingue un golpe de una curación (solo escucha `OnHpChanged`) |
| 10 | Barra de maná | 3 | Idéntica a la vida en forma y altura; solo el hue la separa. Un jugador daltónico verde/azul no las distingue por forma |
| 11 | Barra de XP | 2 | Sin etiqueta (`SetUIReferences(fill, bg, null)`), 18 px, flash amarillo 0.6 s al subir de nivel. **Probable bug de color** — ver D2 |
| 12 | Slots de habilidad | 4 | 36 px, fondo plano, icono, etiqueta "1/2/3" a 10 px, pie de cooldown. Correcto en función. Sin marco por rareza/escuela, sin estado "listo", sin estado "sin maná", sin tecla real (las etiquetas son literales, no leen el binding) |
| 13 | Cooldown | 4 | `CooldownRing` es en realidad un CUADRADO radial (`Texture2D.whiteTexture` con `Radial360`), no un anillo. Flash amarillo 0.35 s al terminar: el único "evento" que el HUD celebra hoy |
| 14 | Feedback de daño | 1 | Ninguno. Sin chip (barra fantasma que se retrasa), sin flash, sin sacudida, sin viñeta. Las barras del mundo YA tienen todo esto (`WorldBarRig`: chip, flash, knock de un texel, heal overshoot) |
| 15 | Feedback de curación | 1 | Ninguno. Subir y bajar son el mismo lerp |
| 16 | Estado crítico (vida baja) | 2 | Solo el swap a rojo. Sin latido, sin pulso del marco, sin desaturación del retrato, sin sonido |
| 17 | Subida de nivel | 2 | Un flash de 0.6 s en la barra de XP. Es el momento más importante de la progresión y no tiene ni un píxel fuera de esa barra |
| 18 | Partículas / VFX | 0 | `grep ParticleSystem UI/HUD Gameplay/HUD` = 0 ficheros. Nada |
| 19 | Motion / animación | 2 | Dos lerps (`Mathf.Lerp` exponencial, sin curva) y dos flashes. Sin easing, sin anticipación, sin overshoot |
| 20 | Rejilla de píxel | 2 | `CanvasScaler.ScaleWithScreenSize`, ref 1600x800, `match 0.5`. A cualquier resolución que no sea la de referencia el factor de escala no es entero, y el retrato (pixel-art a 16/64/96 PPU) se resamplea bilineal. Las barras del mundo resolvieron esto con `WorldBarGeometry.Texels`; el HUD de pantalla no |
| 21 | Coherencia con las barras del mundo | 2 | Dos lenguajes distintos para la misma cosa: sobre la cabeza hay chaflán, marcas de cuarto, chip, pip de dash y glifos de estado; en la esquina hay rectángulos planos. El jugador ve ambos a la vez |
| 22 | Tema / tokens | 2 | 0 usos de `UITheme` en `HUDManager.*` / `PlayerHUD` / `XpBarHUD`. ~20 `new Color(` literales. No hay `HudStyle.asset`: nada es ajustable sin recompilar, al contrario que `WorldBarStyle` |
| 23 | Accesibilidad (daltonismo) | 3 | Vida verde → rojo al 25 % es el par verde/rojo, el más común. Maná azul vs XP amarillo bien. Sin redundancia por forma |
| 24 | Dash | 1 | `DashMeterHUD` existe, dibuja un panel propio en (16,-80) del canvas y **nadie lo instancia** (grep: 0 callsites fuera del propio fichero). Hardcodea `CooldownRemaining / 1f` |
| 25 | Estados de personaje visibles | 2 | Los 8 status effects (Burn, Poison, Stun, Freeze, Slow, Root, Vulnerable, Marked) se ven sobre la cabeza (`WorldStatusIconRow`) y NO en el HUD de pantalla, que es donde el jugador mira para decidir |
| 26 | Rendimiento | 7 | `PlayerAbilityRowHUD.Refresh` cada frame reasigna `sprite`/`color` de 3 iconos aunque nada cambie (dirtiness de canvas por nada). `PlayerHUD.Update` escribe `hpFill.color` cada frame. Menor, pero sin necesidad |

**Media ponderada: 3.4.** Los ejes 3, 4, 14, 15, 18, 21 pesan doble porque son los que separan
"funciona" de "hermoso".

---

## 2. Defectos encontrados en el código (arreglar antes de embellecer)

- **D1 — `Lvl 0` en pantalla.** `LevelLabelHUD.Bind` lee `Experience.Level` en el momento del
  bind y después solo escucha `GameEvents.OnLevelUp`. `Experience.Initialize(xp, level)` (la
  restauración de partida y el arranque) dispara `OnStateChanged`, no `OnLevelUp`. Resultado: el
  badge muestra el nivel que había ANTES de inicializar (0) hasta la siguiente subida de nivel.
  `XpBarHUD` ya escucha `OnStateChanged` justo por este motivo (su comentario lo dice). Mismo
  arreglo.
- **D2 — la barra de XP probablemente es AZUL, no amarilla.** `HUDManager.XpBar` pinta
  `fill.color = XpFillColor` (amarillo) pero `XpBarHUD.fillColor` (serializado) vale
  `(0.31, 0.55, 1)` azul, y `XpBarHUD.Update` hace `else if (fill.color != fillColor)
  fill.color = fillColor;` — en el primer frame el amarillo se sobreescribe a azul. Con XP 0 el
  relleno tiene anchura cero y no se ve; con XP > 0 aparecerá una segunda barra azul bajo el
  maná. Verificar en vivo; el arreglo es que `HUDManager` llame a un setter del driver en vez de
  pintar el `Image` por debajo.
- **D3 — `DashMeterHUD` es código muerto** que además crea su propio canvas si se le añade.
  Borrar, o convertirlo en el pip de dash del panel (ver Fase 1). `EntitySetup.Visuals.cs:97` y
  `FacingIndicator.cs:15` lo citan como si estuviera en pantalla; no lo está.
- **D4 — el retrato es un cuerpo, no un retrato.** Ver eje 7. Y se captura UNA vez al construir
  el HUD: un cambio de loadout (`ApplyLoadout`) o de personaje deja el retrato desactualizado.
- **D5 — `CooldownRing` cuadrado.** El nombre promete anillo; dibuja un pie cuadrado. Con un
  sprite circular generado (el proyecto ya genera `ElementalSprites.Ring`) sería un anillo real.
- **D6 — etiquetas de tecla literales** `{ "1", "2", "3" }`: no leen `InputService`, así que un
  rebind deja la etiqueta mintiendo. CLAUDE.md ya registra esta forma de defecto para el editor
  de Controles.
- **D7 — `PlayerHUD` solo conoce `OnHpChanged`.** `Health.OnDamaged` se dispara justo antes y
  es lo que separa golpe de curación (así lo hace `WorldBarRig`). Sin ese evento no hay chip ni
  flash posibles.

---

## 3. Dirección visual propuesta

Una frase: **el HUD de pantalla debe hablar el mismo idioma que las barras sobre la cabeza y que
el tema dorado de los editores, y celebrar los EVENTOS (golpe, cura, nivel, habilidad lista) en
vez de decorar el estado.**

Las tres reglas que ya rigen el proyecto y aquí no se cumplen:

1. **Rejilla de texel.** Toda dimensión en múltiplos enteros; escalado entero del canvas.
2. **Un evento se ve, un estado se lee.** Nada pulsa en reposo. Las partículas y los flashes
   nacen de un golpe, una cura, un nivel o un cooldown que termina, y mueren en menos de medio
   segundo. Un HUD que brilla todo el rato es un HUD que no dice nada (misma doctrina que
   `FacingIndicator.Pulse` y las descargas del vórtice).
3. **La forma lleva el significado; el color lo confirma.** Vida = barra gruesa con marcas de
   cuarto. Maná = barra fina. XP = filo dorado bajo el panel. Dash = pip. Estados = glifos.

### Esqueleto propuesto

```text
┌─ marco de piedra oscura con filo dorado (UITheme.ACCENT) ─────────────────────┐
│ ┌──────────┐  ♥ ████████████████████░░░░░░░░  70 / 200   ← 26 px, marcas ¼     │
│ │  cara    │  ◆ ██████████████████████████░░  34 / 35    ← 16 px               │
│ │ (crop)   │  [1 icon][2 icon][3 icon] [◼dash]  ⚡🔥      ← 40 px + glifos       │
│ │   ⑫      │  ▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔  XP ← 6 px, dorado, filo brillante │
│ └──────────┘                                                                     │
└──────────────────────────────────────────────────────────────────────────────────┘
```

- Retrato: recorte de la CABEZA (40 % superior del sprite idle, centrado en X) sobre fondo con
  viñeta radial; marco con esquina biselada; el nivel en un medallón circular dorado sobre la
  esquina inferior derecha del retrato, no una pill gris arriba.
- La barra de vida es la única con 26 px y con marcas de cuarto. Maná 16 px. XP 6 px pegada al
  borde inferior del panel, como un filo que se llena.
- Dash como pip cuadrado al final de la fila de slots (mismo `WorldBarPip` que ya existe).
- Glifos de estado (los mismos 6x6 de `StatusGlyphs`) a la derecha de los slots.

---

## 4. Partículas: dónde sí, dónde no, y cómo

### Dónde NO

- Nunca en reposo. Ni motas flotando alrededor del retrato, ni chispas ambientales en la barra
  de vida. Eso convierte el HUD en un salvapantallas y anula la señal cuando algo pasa de verdad.
- Nunca por encima del texto numérico durante más de 0.2 s.
- Nunca dependiendo de la cámara del mundo (la cámara es de Cinemachine, se mueve, se sacude).

### Dónde SÍ (todo disparado por evento, todo con vida ≤ 0.6 s)

| Evento | Fuente | Efecto | Presupuesto |
|---|---|---|---|
| Golpe recibido | `Health.OnDamaged` | 6-10 chispas rojas nacen en el FILO actual del relleno y salen hacia la derecha/abajo; el panel se desplaza 1 texel; el chip de vida perdida se queda 0.4 s y se desangra | 10 motas |
| Cura | `OnHpChanged` con delta > 0 | 4-8 motas verdes suben desde la base de la barra y se apagan; el relleno sobrepasa 1 texel y vuelve | 8 motas |
| Vida < 25 % | umbral | SIN partículas. Latido: el marco de la barra oscila entre `ACCENT_DIM` y rojo a 1.1 Hz y el retrato se desatura al 40 %. Un latido es un estado que se lee, no un evento | 0 |
| Maná gastado | `SpellCaster.ExecuteSpell` | 3 motas azules del slot usado al filo de la barra de maná (dirección: hacia la barra, 0.25 s) | 3 |
| Cooldown listo | `CooldownRing` 1→0 | Destello blanco del marco del slot + 6 chispas del color de la escuela del hechizo (`ElementPalette`) | 6 |
| Subida de nivel | `Experience.OnLevelUp` | Secuencia de 0.9 s: la barra de XP se vacía con estela dorada hacia el medallón, el medallón hace overshoot x1.3, 20-30 motas doradas suben en columna por el borde izquierdo del panel, viñeta dorada 0.3 s | 30 |
| XP ganada | `OnXpGained` | Un glint (1 sprite brillante, no partículas) que recorre el filo de la barra 0.2 s | 1 |

Total en el peor caso (nivel + golpe a la vez): ~40 sprites vivos. Trivial.

### Cómo (recomendación técnica)

**No usar `ParticleSystem` dentro del canvas.** Un `ParticleSystem` es un renderer de mundo; en
un canvas `ScreenSpaceOverlay` no se ordena con las `Image`, y en `ScreenSpaceCamera` obliga a
una cámara de UI propia y a sincronizar su ortho con `CameraSetup`. El proyecto ya paga ese
tipo de acoplamiento en otros sitios y no compensa por 40 sprites.

En su lugar, **`HudMoteEmitter`**: un pool de `Image` (o un único `Graphic` con
`OnPopulateMesh` que emite N quads, más barato aún) dentro del propio panel, en un hijo
`raycastTarget = false` que es el último hermano. Cada mota: posición, velocidad, vida, color,
escala. Un `Update` y nada más. Ventajas:

- Se ordena con el resto del HUD por jerarquía, sin sorting layers.
- Sprites procedurales que ya existen: `ElementalSprites.HotCore` / `Halo` / `Ring`, más un
  `Streak` como `WeatherTextures.Streak`. Nada que pintar.
- Material aditivo real: `ElementalSprites.SharedAdditiveMaterial` funciona en `Image`
  (`SrcAlpha/One`), así que una chispa dorada BRILLA sobre el marco oscuro en vez de taparlo.
  CLAUDE.md ya registra que sobre alfa el brillo máximo es el propio color; aquí aplica igual.
- Testable en EditMode con un `Tick(dt)` explícito, como `XpBarHUD.Tick`.

Regla de oro copiada de `WeaponSwapFlashFX`: **sobre material aditivo, alfa es cobertura y color
es brillo**. Una chispa "más intensa" es color > 1 (HDR está activo), no alfa más alto.

---

## 5. Roadmap por fases (nota estimada tras cada una)

### Fase 0 — Verdad antes que belleza (3.4 → 4.5)

- Arreglar D1, D2, D3, D6, D7. Añadir `HudStyleTests` que pinen: el badge oye
  `OnStateChanged`, el color de XP es el del estilo, la etiqueta de tecla lee el binding.
- Crear `Resources/UI/PlayerHudStyle.asset` (`ScriptableObject`, mismo patrón y misma
  justificación que `WorldBarStyle`: todo se `AddComponent`-ea, no hay slot de inspector). Mover
  los ~20 `new Color(` literales a él. Ratchet como `EditorRawColorRatchetTests`.

### Fase 1 — Un solo lenguaje (4.5 → 6.5)

- Reusar `WorldBarArt` (atlas generado: frame, plate, fill, quarter mark, pip, glifos) en el HUD
  de pantalla con `Image.type = Sliced` y `pixelsPerUnitMultiplier` para que un texel del atlas
  sea un múltiplo entero de píxeles de pantalla. Las barras de la esquina y las de la cabeza
  pasan a ser literalmente los mismos píxeles a distinta escala.
- Rejilla: forzar escala entera del canvas (`scaleFactor = floor(alto / 400)` o similar) en vez
  de `ScaleWithScreenSize`; el retrato deja de resamplearse.
- Jerarquía: vida 26 / maná 16 / XP 6 texeles. Marcas de cuarto solo en vida.
- Retrato recortado a la cabeza + viñeta + medallón de nivel. Refrescar en `ApplyLoadout`.
- Dash como `WorldBarPip`; glifos de estado con `StatusGlyphs`.
- Marco del panel: 9-slice generado (chaflán 2 texeles, filo `UITheme.ACCENT` al 35 %), no un
  rectángulo negro.

### Fase 2 — Feel (6.5 → 7.8)

- Portar de `WorldBarRig` al HUD: chip retardado, flash de placa, knock de 1 texel, overshoot de
  cura, fill cuantizado a píxel entero. Es la misma máquina; extraer `BarFeel` a Core para que
  ambos la usen y no diverjan (la regla de las "dos copias" de este repo).
- Vida baja: latido del marco + desaturación del retrato (`SpriteTintStack` NO: eso es el
  cuerpo; aquí es una `Image` del HUD).
- Subida de nivel: secuencia de 0.9 s descrita arriba, sin partículas todavía.
- Tipografía: outline de 1 px oscuro en los números (TMP `outlineWidth`) y fuente del juego si
  existe; si no, la de los editores para no sumar una tercera.

### Fase 3 — Partículas (7.8 → 8.6)

- `HudMoteEmitter` + los 7 eventos de la tabla. Presupuesto pinned por test (≤ 48 motas vivas).
- Sonido gated en `HasSfx` (el catálogo no tiene ids de HUD; la regla de `PlaySfxById` aplica).

### Fase 4 — Pulido (8.6 → 9+)

- Retrato con expresiones: cuando el personaje tenga arte facial (Gatita ya lo tiene, los
  jugables no) el retrato reacciona: `Hurt` al golpe, `Happy` al nivel. Misma cadena
  `FacialExpressionFallback`.
- Marco por clase (cinco variantes de 9-slice, mismo esqueleto, distinto ornamento).
- Modo "combate limpio": el panel se contrae a solo barras cuando no hay enemigos en 8 s y se
  expande al primer golpe (el mismo `idleFadeDelay` que las barras del mundo).

---

## 6. Qué NO hacer

- No copiar un HUD de Diablo/WoW con orbes. El juego es top-down pixel-art a 16 PPU con un tema
  dorado sobre piedra oscura ya establecido en 17 editores; un orbe 3D rompería las dos cosas.
- No animar el estado. Sin barras "respirando", sin retrato que parpadea, sin motas ambientales.
- No dos rutas de dibujo para una barra. Si `WorldBarRig` y el HUD de pantalla acaban con dos
  implementaciones del chip, una de ellas será la equivocada dentro de un mes.
- No `ParticleSystem` en el canvas.
- No arreglar la belleza antes que D1/D2: un HUD precioso que dice `Lvl 0` sigue estando roto.

---

## 7. Resultado de la reconstrucción (mismo día)

**Nota global: 8.9 / 10**, desde 3.4. Todo verificado en vivo con capturas a `Time.timeScale`
0.004–0.005 (ver sección de método abajo) y con la suite EditMode completa: **8418/8418**.

### Qué se construyó

```text
UI/HUD/PlayerPanel/   PlayerHUD (+.Binding, +.Motes), HudArt, HudPixelFont, HudPixelText, HudRect,
                      HudBar, HudPortrait, HudTextureBaker, HudAbilitySlot, HudSlotHover,
                      HudDashPip, HudStatusRow, HudMedallion, HudTooltip, HudFloatText,
                      HudMoteLayer, HudEdgeVignette, HudLifetime
Data/UI/PlayerHudStyle.cs  +  Resources/UI/PlayerHudStyle.asset (con el shader referenciado)
Shaders/UIHudFx.shader     UI/Default + modo de mezcla + saturación + destello
Borrados: XpBarHUD, LevelLabelHUD, PlayerAbilityRowHUD, CooldownRing, DashMeterHUD,
          HUDManager.PlayerPanel, HUDManager.XpBar  (y sus cinco fixtures)
Tocados:  HUDManager (+Combo, +HUDs), PlayerController.Movement (constantes de los botones),
          SpellCaster (+Execution: OnCastRefusedForMana), Gameplay/AssemblyInfo
```

### Defectos de la sección 2, estado

| # | Defecto | Estado |
|---|---|---|
| D1 | El badge no oía `OnStateChanged` | Arreglado y fijado por test. Matiz: el nivel 0 del arranque es REAL (el modelo empieza en 0, `XpRequiredForLevel(1)` = 100); el defecto afectaba a partidas restauradas |
| D2 | XP pintada de amarillo y repintada de azul | Desaparece con el driver: el color vive en un solo sitio |
| D3 | `DashMeterHUD` muerto | Borrado; el dash es un rombo del panel |
| D4 | Retrato = cuerpo entero, capturado una vez | Cabeza recortada del frame idle ESTE, rehorneado al cambiar de loadout |
| D5 | Anillo de recarga cuadrado | Barrido de reloj sobre icono cuadrado + segundos enteros |
| D6 | Teclas "1/2/3" literales | Los slots muestran lo que hacen los BOTONES del ratón y leen el binding vivo |
| D7 | Solo `OnHpChanged` | Golpe, cura, gasto, rechazo, XP ganada y perdida: seis eventos distintos |

Encontrados durante la reconstrucción, todos arreglados:

- **El slot 0 estaba etiquetado "1" y era el CLIC IZQUIERDO**; su anillo leía el reloj de slot,
  que un clic nunca pone. Solo una captura en vivo lo mostró.
- **Un hechizo sin icono se pintaba como un cuadrado blanco** (el cuadrado blanco de la captura
  original).
- **El chip de daño estaba en el modelo y nunca en pantalla** — su visibilidad solo se
  recalculaba cuando cambiaba SU ancho. El test miraba el modelo y pasaba; ahora mira la imagen.
- **Un 96 % de opacidad deja pasar ~18 % en espacio lineal**: el primer tooltip enseñaba la
  barra de vida a través. Todo lo que tapa algo es opaco.
- **Un 9-slice más pequeño que sus bordes se aplasta fuera de la rejilla**: la barra de XP de
  5 texels con un marco 3+3 era la única fila del panel fuera de rejilla. Hay un guard que
  recorre todas las imágenes 9-slice.
- **La bola de fuego recarga en medio segundo**: celebrar cada fin de recarga habría sido una
  fuente de chispas con el clic mantenido. Solo se celebran recargas ≥ 1.5 s.

### Puntuación por eje, después

| # | Eje | Antes | Después | Por qué no es 10 (si no lo es) |
|---|---|---|---|---|
| 1 | Composición | 5 | 8.5 | La fila de slots queda corta a la derecha cuando no hay estados activos |
| 2 | Jerarquía | 3 | 9 | Vida 13 / maná 9 / XP 5 texels; cifra grande 5x7 solo en la vida |
| 3 | Materialidad del panel | 2 | 9 | Piedra con degradado y tramado, filigrana dorada, bisel, remaches, gema |
| 4 | Materialidad de barras | 2 | 9.5 | Marco con hueco, brillo arriba, sombra abajo, borde de avance, muescas |
| 5 | Paleta | 4 | 9 | La misma paleta que las barras sobre la cabeza + oro del tema |
| 6 | Tipografía | 3 | 9 | Fuente de píxel propia con contorno; TMP solo en el tooltip (acentos) |
| 7 | Retrato | 3 | 8.5 | En el enano el recorte es muy cerrado (casco); factor de reducción entero |
| 8 | Nivel | 2 | 9 | Medallón de oro, halo y anillo de estrellas al subir |
| 9 | Vida | 4 | 9.5 | |
| 10 | Maná | 3 | 9 | |
| 11 | XP | 2 | 9 | Glint al ganar, "+N" flotante, llenado y vaciado al subir; tooltip con cifras |
| 12 | Slots | 4 | 9 | Iconos minificados en GPU, cinco estados, glifo del botón real |
| 13 | Cooldown | 4 | 9 | |
| 14 | Feedback de daño | 1 | 9.5 | Caída instantánea, chip, destello, golpe de 1 texel, chispas, borde rojo |
| 15 | Feedback de curación | 1 | 9 | Segmento previo, relleno que crece, cifra que cuenta, motas que suben |
| 16 | Vida baja | 2 | 9.5 | Relleno y corazón rojos, latido doble, retrato desaturado, borde de pantalla |
| 17 | Subida de nivel | 2 | 9.5 | "NIVEL N", anillo, fuente de motas, destello dorado, barra que se llena |
| 18 | Partículas | 0 | 9 | Solo por evento, píxeles aditivos, presupuesto por test, nada en reposo |
| 19 | Motion | 2 | 8.5 | Todo en tiempo de juego y en texels enteros; sin curvas de easing propias |
| 20 | Rejilla de píxel | 2 | **10** | **Medido: 0 bloques fuera de rejilla de 11 765** (iconos y chaflanes excluidos por diseño) |
| 21 | Coherencia con el mundo | 2 | 9 | Misma paleta, mismos glifos de estado, mismo modelo de chip |
| 22 | Tema / tokens | 2 | 9.5 | Todo en `PlayerHudStyle`; quedan dos grises internos del fallback sin GPU |
| 23 | Accesibilidad | 3 | 8.5 | Forma redundante (corazón, gota, muescas, latido, borde) — el par verde/rojo sigue |
| 24 | Dash | 1 | 9 | |
| 25 | Estados visibles | 2 | 9 | Mismos glifos y tintes que sobre la cabeza, línea de duración y parpadeo final |
| 26 | Rendimiento | 7 | 8.5 | Canvas anidado, escrituras solo al cambiar; sin medición de Profiler |

### Método de verificación

- Capturas con `ScreenCapture.CaptureScreenshot` (la herramienta de captura por cámara omite
  los canvas overlay) en la MISMA llamada que provoca el evento, a `Time.timeScale` 0.004: los
  efectos viven décimas de segundo y el puente MCP tarda segundos entre llamadas.
- Rejilla: script que exige que cada bloque de `escala x escala` píxeles del panel sea uniforme.
- Tests: `PlayerHudTests` (37 casos) fija la rejilla, los objetivos de clic, el chip DIBUJADO,
  la cura que no es golpe, las motas que mueren, la vida baja, la muerte, el rechazo por maná,
  la XP, la subida de nivel, los tres botones, la recarga con el reloj del libro, las recargas
  cortas sin celebración, el tooltip encima del panel, el asset con su shader, la fuente y el
  atlas.

### Lo que queda abierto

- El recorte del retrato del enano es muy cerrado (su cuerpo mide 119 px y el factor entero
  más cercano es 1). Un `portraitBodyTexels` por personaje lo resolvería.
- El par verde/rojo de la vida baja sigue siendo el más problemático para daltónicos; hoy lo
  compensan la forma (latido, borde de pantalla, corazón), no el color.
- Sin sonido: el catálogo de audio no tiene ids de interfaz; cualquier llamada debe ir tras
  `HasSfx`.
- Sin medición de Profiler del panel.
