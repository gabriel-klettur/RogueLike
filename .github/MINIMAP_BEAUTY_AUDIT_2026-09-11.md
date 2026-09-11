# Auditoría de belleza del minimapa — 2026-09-11

> Nota global: **3.4 / 10**. El hallazgo que enmarca todo lo demás: **no es un mapa,
> es un radar sobre un disco negro.** No dibuja ni un píxel de terreno, de agua, de
> camino ni de edificio. El comentario de cabecera de `MinimapManager` promete una
> "Background tile layer (static, rate-limited)"; ese layer nunca se portó de Python —
> `tileRedrawInterval` y `_lastTileRedraw` llevan un `#pragma warning disable CS0414`
> encima porque nada los lee.

## Método

Medido en vivo, 2026-09-11, 1600x800, ortho 8.33, jugador en `(175, 90)` de Lobby,
`viewRadius` 39.4 (el jugador había hecho zoom out desde el 24 por defecto):

- Rect de cada pieza del widget leído por `GetWorldCorners` a través de `execute_code`.
- Volcado de la `Texture2D` interna (160x160) y recuento por color: **20 298 px niebla,
  4 495 px fondo, 807 px "otra cosa"** (marcadores + puntos). El 79 % del disco es niebla.
- Captura de pantalla y recorte del disco a 3x (`minimap_crop_x3.png`, en el scratchpad
  de la sesión; enviada al usuario).
- Coste de un redibujado: `LateUpdate` forzado 50 veces con Stopwatch.
- Lectura de los seis ficheros del subsistema (1 268 líneas), de los cinco sitios que
  registran marcadores y de los siete tests que lo cubren.

## Notas por eje

| # | Eje | Nota | Una línea |
|---|---|---|---|
| 1 | Contenido cartográfico (terreno, agua, caminos, edificios) | **0** | No existe. Disco negro con puntos |
| 2 | Niebla de guerra: legibilidad | 2 | Explorado `(0.06,0.06,0.10)` contra no explorado `(0.03,0.03,0.05)`: tres centésimas de diferencia. La "zona explorada" es una mancha gris que hay que buscar |
| 3 | Marco: disco + anillo | 6 | Círculo antialiasado generado en código, anillo dorado `ACCENT` plano al 55 %. Sin bisel, sin sombra proyectada, sin borde interior, sin viñeta |
| 4 | Coherencia con la familia HUD (reloj, barras) | 7 | Mismo lenguaje de dial que `DayNightClockHUD`, margen y escala compartidos por `HudLayout`. Lo mejor del widget |
| 5 | Marcador del jugador | 5 | Flecha blanca de 14 px, bilineal, sin halo ni cono. Debajo se dibuja un punto verde de 3 px cada redibujado que nadie ve |
| 6 | Puntos de entidad | 2 | **Los seis vendedores son rojos**: `EntitySetup.ConfigureMonster` da `"Monster"` a todo NPC (`EntitySetup.cs:210`), el tipo `NPC` amarillo existe y ningún camino lo usa. Y un enemigo fuera de rango se pega al borde del CUADRADO de la textura mientras el disco es un CÍRCULO: `Barbol Oscuro` en `(202, 2)` se dibuja en `(135, 1)`, fuera del círculo, y desaparece |
| 7 | Marcadores de mundo (portal, puerta, vendedor, salida) | 4 | Diamantes y cuadrados de 4-5 px sin iconografía. Un vendedor es un cuadrado dorado con un punto rojo encima: dos sistemas apilados que se contradicen |
| 8 | Marcadores de misión | 2 | `QuestOffer` es un diamante dorado de 5 px dibujado ANTES del cuadrado dorado de 5x5 del vendedor que la ofrece. **Tapado al 100 %**: en la captura, GA y SM ofrecen misión y no se ve nada. Sólo el `Plus` de 7 px de la entrega sobresale |
| 9 | Jerarquía visual | 3 | Ningún glifo lleva contorno ni sombra; el único tamaño por importancia es el turn-in. Todo compite en la misma capa |
| 10 | Tipografía y plato de información | 6 | "Lobby" en 13 negrita legible. Las coordenadas `X 175 Y 89` son de desarrollador, no de jugador. Sin icono de zona, hora ni clima |
| 11 | Movimiento y animación | 2 | Redibujado a 12 Hz: los puntos saltan. El único movimiento es el pulso del vendedor, un `PingPong` lineal. El zoom salta por detent sin ease |
| 12 | Respuesta al mundo (día/noche, clima, interior, espíritu) | **0** | No lee `DayNightCycle`, `WeatherManager`, `ZoneManager.IsDetectionSuspended` ni el estado espíritu. En un interior sigue diciendo la zona exterior y acumula niebla sobre una sala que no está en el mapa |
| 13 | Zoom e interacción | 5 | Rueda geométrica, persistida, hit-shape circular: bien hecho. Sin indicador de escala, sin ping, sin mapa completo, sin ocultar |
| 14 | Rendimiento | 4 | **4.26 ms por redibujado** (25.6k `SetPixel` + 25.6k búsquedas en `HashSet` para la niebla), 12 veces por segundo. Cero asignación. Es un pico de 4 ms cada cinco frames, no un coste amortizado |
| 15 | Resolución y nitidez | 4 | Textura de 160 px mostrada a 172 px con `FilterMode.Point`: escala 1.075, filas duplicadas a intervalos irregulares. Un tile es 2.2 px a este zoom |
| 16 | Partículas y vida | **0** | Ninguna. Sin motas, sin ping, sin destello al descubrir, sin latido |
| 17 | Accesibilidad | 3 | Rojo / verde / dorado sólo por color; todas las entidades son el mismo cuadrado |
| 18 | Arquitectura y extensibilidad | 6 | Manager / HUD / Dot / Marker / Board bien separados y el cruce de ensamblados resuelto. Pero los colores están duplicados en cuatro sitios (`EntitySetup` literal, defaults del manager, cada registrador) y el "tile layer" prometido no existe |
| 19 | Tests | 4 | Siete tests, todos de registro, pulso y niebla en aislamiento. Ninguno de la COMPOSICIÓN: vendedor rojo, misión tapada, clamp fuera del círculo. Es la forma que CLAUDE.md ya registra una docena de veces |

Media aritmética: 65 / 19 = **3.4**.

## Los cinco defectos con número

1. **El terreno es 0 px.** `MinimapManager.LateUpdate` hace `SetPixels(_bgPixels)`,
   pinta niebla, borde, marcadores y puntos. No consulta `WorldGridBuilder`,
   `BuildingLoader` ni ningún ruleset. Todo lo que el jugador aprende del mundo lo aprende
   de la niebla, y la niebla apenas se ve (punto 2).
2. **Vendedores rojos.** Los doce `MinimapDot` vivos son once `Monster` y un `Player`;
   `Pavel`, `Valeria`, `Roberto`, `Abigail`, `Smith` y `Gatita` llevan
   `RGBA(0.90, 0.20, 0.20)`. En pantalla: cuadrado dorado con centro rojo.
3. **La oferta de misión está tapada.** Orden de dibujo: marcadores de componente,
   marcadores del `WorldMarkerBoard`, puntos. El diamante de 5 px de `QuestOffer` queda
   bajo el cuadrado de 5x5 del vendedor, del mismo dorado, y bajo el punto rojo.
4. **El clamp es cuadrado, el disco redondo.** `px = Clamp(px, half, texWidth-half-1)`;
   las esquinas del cuadrado quedan fuera del `Mask` circular. Un enemigo a 88 unidades al
   sur-este se dibuja en la esquina inferior derecha del cuadrado y no se ve.
5. **4.26 ms por redibujado**, cuando un `RawImage.uvRect` sobre una textura horneada
   costaría cero.

## Sobre usar partículas en el minimapa

Hoy: 0. Y la forma obvia de añadirlas es la incorrecta.

- **No un `ParticleSystem` sobre el canvas.** `MinimapHUDCanvas` es `ScreenSpaceOverlay`;
  un `ParticleSystem` es un renderer de mundo, no respeta el `Mask` circular del disco, no
  se ordena contra los `Graphic` del canvas y obligaría a un segundo canvas
  `ScreenSpaceCamera` con sorting propio. El proyecto no lleva ningún plugin de
  partículas-en-UI y no conviene añadir uno para un disco de 172 px.
- **Sí un `MaskableGraphic` propio.** Es el patrón que `SparklineGraphic` y
  `TriangleHandleGraphic` ya usan: un mesh de N quads reconstruido en `OnPopulateMesh`,
  respeta el `Mask`, entra en el batch del canvas, cero draw calls extra y sus coordenadas
  son las del disco. Con 64 quads de presupuesto se cubren todos los eventos de abajo.
  Rebuild sólo mientras haya motas vivas: en reposo, cero.
- **La regla es la de `FacingIndicator`:** una partícula responde a *qué acaba de pasar*,
  nunca a *en qué estado estoy*. Un estado hay que leerlo; un evento confirma lo que el
  jugador ya sabe, en el sitio al que ya está mirando.

Eventos que merecen partículas, en orden de valor:

| Evento | Efecto | Por qué |
|---|---|---|
| Se revela niebla nueva | 8-14 motas doradas en la frontera recién descubierta, 0.6 s, ease-out | Convierte "explorar" en algo que se ve pasar |
| Aparece o se completa un objetivo | Anillo que se expande desde el glifo, 0.4 s | Es el único momento en que el mapa tiene algo NUEVO que decir |
| El jugador recibe daño | El anillo exterior parpadea rojo y tres chispas salen DESDE la dirección del atacante | La dirección es información; el rojo es confirmación |
| Latido del sonar | Anillo tenue desde el jugador cada 2-3 s, alcanza el borde | Da escala sin un texto "1 tile = 2 px" y hace que el disco esté vivo |
| Portal, altar y puerta | Halo que respira a 0.35 Hz | Los destinos se distinguen de las cosas |
| Ambiente nocturno | 6-10 motas lentas al 15 % de alpha, sólo de noche | De día ninguna: la ausencia es lo que hace que la noche se note |

## Cómo llegar a profesional: seis fases

Cada fase se puede enviar sola. Notas esperadas al final de todas: contenido 8, niebla 8,
marco 8, jugador 8, entidades 8, marcadores 8, misiones 8, jerarquía 8, animación 8,
respuesta al mundo 8, rendimiento 8, partículas 8. Media estimada **7.8**.

### Fase 0 — Corregir lo roto (medio día)

- Tipo de punto por facción: `Neutral` → `NPC`; un vendedor con marcador propio no
  necesita punto. Una fuente de color: el manager, y `EntitySetup` deja de pasar literales.
- Proyectar al borde del CÍRCULO, no del cuadrado (`radio = texWidth/2 - half`), con un
  chevrón para "está fuera". Un enemigo fuera de vista se pega al borde, no desaparece.
- Un vendedor con misión es UN glifo con una insignia en la esquina, no dos superpuestos.
  Como mínimo, dibujar el board DESPUÉS de los puntos.
- Contraste de niebla: explorado al menos 0.20 más claro que no explorado.
- Hooks al mundo: `ClearFog` también al entrar en interior; etiqueta de zona desde
  `IsDetectionSuspended`; tinte por `DayNightCycle.CurrentColor`.
- Tests de composición: los tres defectos de arriba, sobre el manager real.

### Fase 1 — Terreno (de 0 a 7 en el eje que más pesa)

Hornear un **mapa de color de 1 px por tile**, una vez por zona cargada:

- Para cada celda, el sprite más alto de las capas visuales
  (`WorldGridBuilder.GetTilemap` de Ground a ObjectsHigh), color medio por sprite con caché
  por sprite. Cuatro zonas de 50x50 son ~200x150 px: trivial.
- Edificios: `BuildingLoader.SpawnedBuildings` + `BuildingObject.TryGetWorldRect`, pintar
  el footprint (`1 - splitRatio`) en tono muro con una línea de sombra al sur.
- Agua y lava por nombre de terreno del ruleset (`rock_water`, `rock_lava`).
- El minimapa pasa a ser una VENTANA sobre esa textura via `RawImage.uvRect`: el redibujado
  de fondo desaparece, el desplazamiento es por frame y gratis, y la textura puede ser
  bilineal sin aliasing de escala 1.075.
- La niebla pasa a una segunda textura `R8` (máscara de explorado) combinada en un shader
  de UI, `Valkur/MinimapComposite`, en lugar de 25.6k `SetPixel`.

### Fase 2 — Sombreado y marco

- En el shader compuesto: viñeta radial, borde interior oscuro de 2 px, frontera de niebla
  suavizada 1-2 texels con un filo dorado tenue, tinte por hora al 35 %, lluvia y nieve como
  desaturación más grano.
- Anillo con bisel (luz arriba-izquierda, sombra abajo-derecha), sombra proyectada exterior
  de 4 px, marcas cada 45°, cardinales con contorno.
- Plato: icono de zona más clima; las coordenadas se van a `verbose` o al DevConsole.

### Fase 3 — Glifos

- Atlas de iconos de 9x9 / 11x11 generado en código, el precedente es `WorldBarArt`:
  portal (anillo), puerta (arco), vendedor por oficio (martillo, hoja, olla, moneda),
  misión (`!` / `?`), altar (cruz), cadáver (calavera), jefe (corona), aliado (escudo).
- Contorno de 1 px oscuro bajo cada glifo. Es lo único que hace legible un icono sobre
  cualquier terreno, y es lo que hoy no lleva ninguno.
- Los altares, el cadáver, los nodos de cosecha y los puestos de crafteo no están en el
  minimapa; el sistema de la muerte dibuja su propio rastro en el mundo y ninguno de los
  dos sabe del otro.

### Fase 4 — Movimiento

- Interpolar la posición de los puntos entre redibujados, o mejor: con el fondo en
  `uvRect`, redibujar sólo la capa de puntos y hacerlo a 30 Hz.
- Zoom con ease de 120 ms; pulsos con ease-out, no `PingPong` lineal.

### Fase 5 — Partículas y vida

La tabla de arriba, sobre un `MinimapMoteGraphic : MaskableGraphic`. Presupuesto 64 quads.

### Fase 6 — Mapa completo

Tecla para el mapa a pantalla completa reutilizando la misma textura horneada: zonas con
nombre, leyenda, escala, niebla compartida. Es la fase que convierte el disco en un sistema.

## Medidas de referencia

- Widget: `x[1384..1576] y[540..776]`, disco 192 px, mapa 172 px, plato 40 px.
- Textura 160x160 `RGBA32` Point; 1 unidad de mundo = 2.18 px a radio 39.4, 3.6 px a 24.
- Redibujado: 4.255 ms, 0.0 KB, 1 102 celdas exploradas; 12 puntos, 6 marcadores, 2 board.
- Colores: disco `(0.06,0.07,0.10,0.94)`, anillo `(0.90,0.76,0.38,0.55)`, cardinales
  `(0.95,0.85,0.45)`, etiqueta de marcador `(1.00,0.96,0.78)` TMP 9 negrita sin contorno.
- `Valkur.UI` referencia `Valkur.Gameplay` en su asmdef: el minimapa PUEDE leer el mundo
  directamente. La reflexión es sólo en la dirección contraria (`EntitySetup` hacia UI).

---

## Reconstrucción — mismo día

Todo lo anterior describe el minimapa que había. Esta sección describe el que hay. Las seis
fases se hicieron; la tabla compara eje por eje y la columna "por qué no 10" dice qué falta.

| # | Eje | Antes | Ahora | Por qué no 10 |
|---|---|---|---|---|
| 1 | Contenido cartográfico | 0 | 9 | El horneado ve árboles talados y nieve caída hasta el siguiente rehorneado del trozo |
| 2 | Niebla de guerra | 2 | 9 | El eco del terreno bajo la tinta es una decisión de gusto: 0.045 enseña formas, no detalle |
| 3 | Marco | 6 | 9 | Arte generado, no pintado a mano |
| 4 | Coherencia con la familia HUD | 7 | 9 | El reloj de arriba a la izquierda sigue con su anillo plano |
| 5 | Marcador del jugador | 5 | 9 | — |
| 6 | Puntos de entidad | 2 | 9 | Sin estado de persecución (decisión: la regla de "eventos, no estados") |
| 7 | Marcadores de mundo | 4 | 9 | Los nodos de cosecha no tienen icono propio |
| 8 | Misiones | 2 | 9 | — |
| 9 | Jerarquía visual | 3 | 9 | — |
| 10 | Tipografía y placa | 6 | 9 | — |
| 11 | Movimiento | 2 | 9 | — |
| 12 | Respuesta al mundo | 0 | 9 | La niebla de un interior no distingue dos casas con el mismo nombre de zona |
| 13 | Zoom e interacción | 5 | 9.5 | — |
| 14 | Rendimiento | 4 | 9 | El horneado inicial son ~117 trozos a 3.7 ms, repartidos por presupuesto |
| 15 | Resolución y nitidez | 4 | 9 | Atlas a 8 px/unidad: a zoom máximo hay un ligero escalado bilineal |
| 16 | Partículas y vida | 0 | 9 | — |
| 17 | Accesibilidad | 3 | 8 | Forma + contorno + tooltip; no hay modo daltónico configurable |
| 18 | Arquitectura | 6 | 9 | — |
| 19 | Tests | 4 | 9 | El horneado por cámara no tiene test automático (necesita GPU) |

Media: **9.0** (antes 3.4).

### Qué se construyó

- **Terreno horneado por cámara** (`MinimapWorldBaker`). El arte real del mundo —tiles y
  edificios con su orden de dibujo— en un atlas de 8 px/unidad, por trozos de 32×32, el más
  cercano primero, con presupuesto por frame. Todo lo que no es mundo se oculta por lista
  blanca durante el `Camera.Render`, y la luz se neutraliza en el mismo intervalo.
- **Shader compuesto** (`Valkur/UI/MinimapComposite`): tinta de bordes, saturación y
  contraste, zona vista contra recordada, rejilla, tinte de día/noche, lluvia y nieve, niebla
  con nubes que derivan y sombreado de cartógrafo, frontera dorada, brillo sobre el agua,
  sonar y viñeta. El círculo se antialiasa en el shader: no hay `Mask`.
- **Niebla por mundo** (`MinimapFogMap`): exterior y cada interior por separado, borde suave,
  persistida por partida y fusionada al cargar.
- **Glifos** (`MinimapIconAtlas`): 28 iconos generados con SDF a dos tonos. Oficio por
  comerciante, calavera para jefes, escudo para aliados, `!` y `?` de misión, puerta, portal,
  altar, tumba, pin.
- **Reglas de visibilidad** (`MinimapReveal`): criaturas sólo a la vista, lugares una vez
  explorados, misiones y destino siempre.
- **Partículas como eventos** (`MinimapFx`), clima dentro del disco, y chispas desde la
  dirección del golpe.
- **Mapa del mundo** (`WorldMapPanel`, tecla N o clic en el disco): arrastrar, zoom al cursor,
  pin con clic, leyenda, nombres de zona explorada, lectura bajo el cursor.
- **Tooltip** sobre cualquier icono y **distancia** en cada pin de borde.
- **Consola**: `minimap [rebake|reveal|fog clear|pin|zoom]`.

### Medido

- Coste por frame en régimen: **0.10 ms**; 0.18 ms con tormenta y 46 partículas vivas (antes,
  picos de 4.26 ms cada cinco frames).
- Tests: 55 en los ocho fixtures del minimapa y del mapa del mundo, más ZoomContract, el
  ratchet de estáticos, input y convenciones de assets: 478/478 en verde.
- Verificado en vivo: Lobby de día, noche con tormenta, forma espiritual con altar y cuerpo, y
  el mapa del mundo con pin, leyenda y nombres de zona.
- Horneado: 117 trozos, 8 px/unidad, mundo 416×288 unidades; 3.7 ms por trozo.
- Interiores: la sala se revela entera al entrar, los pins del exterior no se cuelan, y al salir
  vuelve la niebla del pueblo intacta.
- De paso: el arranque inyectaba el catálogo de hechizos después de que los spawners colocaran
  monstruos, así que el dragón y el enano oscuro nacían sin hechizos y dejaban dos avisos en
  cada arranque. Ahora se inyecta al principio de `GameplaySceneSetup.Start`.
- Defectos encontrados al verlo en vivo y corregidos: glifos invisibles (faltaba
  `CanvasRenderer`), esquinas blancas en la placa (mips sin escribir), iconos visibles bajo la
  niebla, agua con brillo de ruido, `line` como identificador HLSL reservado.
