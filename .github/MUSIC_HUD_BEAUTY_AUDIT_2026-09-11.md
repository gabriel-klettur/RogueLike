# Auditoría de belleza del reproductor de música (MusicPlayerHUD)

**Fecha:** 2026-09-11 · **Nota global: 2.1 / 10** (ponderada; media aritmética 2.3) ·
Objetivo: **≥ 8.5**

Alcance: el widget de abajo a la derecha que construye `MusicPlayerHUD`
(`UI/HUD/MusicPlayerHUD.cs`, `.UIBuilder.cs`, `.SpriteFactory.cs`, `.Spectrum.cs`,
`.Waveform.cs`, `.Interaction.cs`, 2 078 líneas), en sus dos modos (compacto y expandido), y
su botón en la bandeja (`HUDIconBar`). Se citan por coherencia el panel del jugador (8.9 tras
su reconstrucción de hoy), el minimapa (9.0), la barra de hechizos (en reconstrucción) y el
registro de misiones, que es la otra ventana del HUD.

El lenguaje visual común del HUD está en [`HUD_VISUAL_LANGUAGE.md`](HUD_VISUAL_LANGUAGE.md)
(escrito hoy por otra sesión, R1-R12). La sección 7 dice qué le falta para cubrir paneles como
este y cómo convertir la coherencia en algo que un test compruebe.

---

## Método

Medido en vivo el 2026-09-11 en Play Mode, 1600x800, con `execute_code`:

- Rect de cada pieza en píxeles de pantalla (`GetWorldCorners`), escala del canvas, escala
  local del widget, fuente y escala efectiva de cada TMP.
- Dos capturas con `ScreenCapture.CaptureScreenshot` (la herramienta de captura por cámara
  omite los canvas overlay), una por modo, abriendo el panel por reflexión SIN escribir
  PlayerPrefs y devolviéndolo después a su estado (oculto, compacto). Recortes a 3x y 2x en el
  scratchpad de la sesión.
- Coste de `Update` con Stopwatch (200 llamadas por modo).
- La fuente de audio viva: volumen, pico de `GetOutputData` y suma de `GetSpectrumData`.
- Datos enviados: `loadType` de las 24 pistas de `Audio/Music/` y el campo `bpm` de las 24
  entradas de `Resources/AudioCatalog.asset`.
- Posición medida del minimapa, del panel del jugador y de la bandeja, para los solapes.

---

## 1. Resumen

El panel tiene buenas ideas de fondo: el volumen se guarda en un solo sitio
(`GameSettings.musicVolume`, compartido con los menús), la barra se puede arrastrar para
buscar, y hay tap-tempo, rejilla de compases y analizador. Pero **la mitad de lo que dibuja no
funciona con los datos que se envían, y la otra mitad está deformada.**

- **Está estirado.** El tamaño se cambia con `localScale` independiente en X e Y. Medido:
  **(0.56, 0.87)**, así que cada letra, icono y círculo sale un **57 % más alto que ancho** de lo
  diseñado. El tirador del volumen es un óvalo y los botones miden 29 x 38.5 px.
- **La capa de tempo nunca se ha encendido.** Las **24 de 24** pistas tienen `bpm: 0`. El
  metrónomo, el contador "Bar · Beat", los puntos de pulso y la rejilla de compases no han
  mostrado nunca un dato real. El punto azul que se ve junto al título es un metrónomo parado.
- **El modo expandido está muerto con el juego silenciado.** Lee la señal después del volumen
  de la fuente. Medido con el volumen de música a 0: pico de salida 0.0000 y espectro 0.000013.
  Resultado: una losa negra con un cabezal rojo y una fila de puntos azules planos. Y como las
  24 pistas son `Streaming`, la forma de onda "de la canción entera" nunca existe de entrada:
  se pinta mientras suena.
- **Iconos rotos.** La pausa se dibuja como dos puntitos arriba y las flechas como `'◀` y
  `▶'`: los argumentos de `FillRect` están en orden cambiado. El chevrón de expandir apunta al
  revés.
- **Fuera del HUD.** Su canvas escala contra 800x600 (factor 2.0 contra el 1.0 del resto del
  HUD), comparte el orden 150 con la barra de hechizos y el modo expandido tapa el **63 % del
  minimapa**. No usa ni una pieza del kit del panel del jugador, y habla inglés en un juego en
  español.

La conclusión de fondo es la misma que en la barra de hechizos: **primero verdad, después
belleza**. Y una decisión de diseño previa: **el HUD del jugador no es un DAW.** El tap-tempo,
el arrastre de BPM y la rejilla son herramientas de autoría, y su sitio es un editor.

---

## 2. Puntuación por eje

| # | Eje | Nota | Evidencia |
| --- | --- | --- | --- |
| 1 | Verdad de los datos mostrados | 1.5 | 24/24 pistas con `bpm: 0`: metrónomo, compás, puntos de pulso y rejilla vacíos desde siempre. La fila de pulso dibuja 4 puntos de un tempo que no existe. El analizador y la forma de onda leen señal post-volumen: silencio = pantalla vacía |
| 2 | Proporción (escalado) | 1 | `ApplyScaleFromSize` (`Interaction.cs:71-78`) pone `localScale = (w/320, h/78)`. Medido (0.56, 0.87): deformación 1.57x. En expandido (0.94, 0.78): deformación al revés. El jugador puede dejarlo en cualquier proporción |
| 3 | Rejilla de píxel | 1 | Canvas propio a factor 2.0 (ref 800x600, `match 0`) por una escala libre: ningún borde cae en un píxel entero. Iconos de 32 px bilineales dibujados a 15.6 x 24.5 px |
| 4 | Composición | 3 | La pista de progreso atraviesa "no tempo" (texto y. 257-278, pista y. ≈ 260). El rect del título ocupa todo el ancho y pisa 165 px del contador de compás. El metrónomo invade la cabecera 7 px. El botón de cerrar sobresale de la cabecera. La cabecera es el 23 % de la altura y solo lleva un `/` y un `—` |
| 5 | Jerarquía | 3 | El título manda, bien. Pero el tiempo, el meta y el contador tienen el mismo peso, y los cinco botones son cajas iguales: play (el control principal) es igual que mute |
| 6 | Materialidad del panel | 1.5 | Fondo `Image` sin sprite a `(0.06,0.06,0.08,0.85)`: rectángulo recto y translúcido, con el mundo visible detrás (en espacio lineal un 15 % de hueco deja pasar mucho más). Sin contorno, sin bisel, sin esquinas, mientras los botones sí son redondeados |
| 7 | Iconografía | 1.5 | Pausa, anterior y siguiente mal dibujados (defecto D2). Chevrón invertido. El altavoz tiene el cuerpo descolocado. El tirador de tamaño es una `/` de TMP. Cerrar usa `—`, el mismo glifo que "minimizar" en el registro de misiones |
| 8 | Tipografía | 2.5 | LiberationSans sin contorno, estirada 1.57x. Meta a 10 pt en azul al 95 %, tiempo al 85 %. El panel de al lado usa la fuente de píxel 3x5 / 5x7 con contorno horneado |
| 9 | Paleta | 3.5 | Oro `(0.95,0.78,0.25)`: tercera deriva del oro del tema `(0.90,0.76,0.38)`. Además azul de metrónomo, rampa azul a magenta en el analizador (el comentario dice "gold → magenta"), cabezal rojo y onda color arena `(220,180,90)`. Cinco tonos sin sistema, 62 `new Color(` literales |
| 10 | Controles de transporte | 3 | Funcionan. Fondo blanco al 10 % casi invisible, hover amarillo sobre ese 10 %, deshabilitado gris al 40 %. Sin estado pulsado legible |
| 11 | Barra de progreso | 4 | Buena zona de clic (12 unidades sobre una pista de 3) y arrastre para buscar. Sin cabezal, sin hover, sin previsualización del tiempo bajo el cursor |
| 12 | Volumen | 3.5 | Arquitectura correcta (una sola fuente de verdad). Tirador ovalado, sin cifra, silencio indicado solo por el icono |
| 13 | Modo expandido | 2 | 603 x 500 px: el 23.6 % de la pantalla. Tapa el 63 % del minimapa. Vacío con el juego silenciado. 64 `Image` reconstruyendo el canvas cada frame. Tap-tempo y arrastre de BPM sin ninguna pista visual de que existen |
| 14 | Feedback de eventos | 2 | Solo uno: al cambiar de pista, todo el fondo pasa a oro al 85 % y vuelve en 0.6 s (una losa dorada). Nada al pausar, saltar, buscar ni silenciar |
| 15 | Partículas | 0 | Ninguna |
| 16 | Movimiento | 2 | Abrir y cerrar es un salto: `ApplyPanelVisibility` pone el alfa de golpe, así que el `MoveTowards` de `Update` nunca tiene nada que animar. Pulso lineal sin easing |
| 17 | Gramática de ventana | 2 | No se puede arrastrar. El tirador de tamaño está arriba a la izquierda y es una `/`; en el registro de misiones es un triángulo abajo a la derecha. Cerrar es `—` aquí y `✕` allí, y `–` significa minimizar allí. Sin título en la cabecera |
| 18 | Coherencia con el HUD | 1.5 | Cero piezas de `HudArt`, `HudPixelText`, `HudRect` o `HudMoteLayer`. Canvas fuera de `HudLayout`. Orden 150, el mismo que la barra de hechizos |
| 19 | Identidad musical | 2.5 | Solo el título. Sin sigilo de zona, sin "3/14", sin "a continuación". El nombre ya lleva la zona ("Desert Theme 3") y no se aprovecha |
| 20 | Descubribilidad | 2 | Oculto por defecto en la primera partida, y el icono de la bandeja se carga con `AssetDatabase.LoadAssetAtPath` (`MusicPlayerHUD.cs:98-108`): en un build es null y la bandeja muestra un cuadrado gris. En un build el reproductor es prácticamente inencontrable |
| 21 | Accesibilidad | 3 | Texto pequeño y de poco contraste, estados solo por color, sin atajo de teclado para play, pausa o siguiente |
| 22 | Idioma | 1 | "No music", "no tempo", "paused", "Bar · Beat" en inglés; el resto del HUD dice "Misiones", "NIVEL N", "Conversar" |
| 23 | Respuesta al estado del juego | 1 | No reacciona a la forma de espíritu (R12 del lenguaje visual), ni al combate, ni a entrar en un interior |
| 24 | Arquitectura / tokens | 3.5 | Los partials están bien separados y la persistencia es ordenada. Pero: 62 colores literales, sin asset de estilo, 12 sprites estáticos en `unreset-statics.txt` e iconos generados por el propio widget en vez de por un atlas compartido |
| 25 | Rendimiento | 5 | Script barato: **2.5 µs** por `Update` en compacto y **115 µs** en expandido. Pero `Update` corre entero con el panel oculto (alfa 0): reescribe 4 textos por frame y, si quedó expandido, 64 barras. Sin canvas anidado. La onda sube 196 KB a la GPU a 30 Hz |
| 26 | Tests | 3 | Dos fixtures, `MusicPlayerHUDVolumeTests` (10) y `MusicPlayerHUDVisibilityTests` (12). Ninguno de composición: escala, solapes, iconos, contrato de canvas |

**Media aritmética: 59.5 / 26 = 2.3. Ponderada: 2.1.** Pesan doble los ejes 2, 3, 6, 7, 14, 15
y 18, los mismos criterios que en las auditorías del panel y del minimapa: son los que separan
"funciona" de "hermoso".

---

## 3. Defectos, en orden de gravedad

- **D1 — Escalado no uniforme.** `Interaction.cs:71-78`. Todo el contenido se diseña a 320x78
  y se estira con `localScale` independiente en X e Y. Es la causa del texto comprimido, el
  tirador ovalado y los botones más altos que anchos. Arreglo inmediato: escala uniforme
  (`min(sx, sy)`). Arreglo real (fase 1): redimensionar cambia la MAQUETACIÓN en pasos de texel
  y nunca la escala.
- **D2 — Tres iconos mal dibujados.** `SpriteFactory.cs:175`: la firma es
  `FillRect(px, N, w, h, x0, y0)`, y la pausa (`:93-94`) pasa `8, 6, 5, IcoN - 12`: un bloque de
  8x6 en y=20 en vez de una barra de 5x20. Lo mismo en anterior/siguiente (`:103`, `:109`, la
  barra vertical sale como una tilde de 3x6 arriba) y en el cuerpo del altavoz (`:119`). Además
  `BuildChevronSprite(pointUp: true)` (`:136-154`) pone el vértice DEBAJO de los brazos en
  coordenadas de textura (y crece hacia arriba): dibuja una V, que apunta abajo.
- **D3 — La capa de tempo no tiene datos.** 24/24 `bpm: 0`. `tools/audio/analyze_music.py` y
  `patch_audio_catalog_bpm.py` existen y ningún commit ha escrito nunca un BPM. Mientras tanto
  el panel no debería dibujar lo que no sabe: puntos de pulso, contador y metrónomo solo con
  BPM > 0. El metrónomo no se resetea a gris cuando el reloj está inactivo
  (`MusicPlayerHUD.cs:406-410`): por eso es un punto azul permanente.
- **D4 — Visualizador post-volumen y onda progresiva.** `GetOutputData` / `GetSpectrumData` de
  un `AudioSource` miden la señal después de `source.volume`. Medido: volumen 0, pico 0.0000.
  Las 24 pistas son `Streaming`, así que la ruta `GetData` de la onda completa es código que no
  corre nunca, y la onda solo existe como historial de lo que ya ha sonado (con volumen).
- **D5 — El modo expandido tapa el minimapa.** Expandido ocupa x 965-1568, y 208-708; el
  minimapa está en x 1377-1587, y 540-781. Solape de 191 x 168 px: el **63 %** del disco, y el
  reproductor (150) se dibuja ENCIMA del minimapa (105). El registro de misiones, que cuelga
  bajo el minimapa, queda tapado también (CLAUDE.md ya lo registró como un 35.1 % cubierto).
- **D6 — Canvas fuera de contrato.** `UIBuilder.cs:18-27` crea `MusicHUDCanvas` con el
  `CanvasScaler` por defecto (800x600, `match 0`): factor 2.0 contra el 1.0 del resto del HUD.
  Es el mismo defecto que ya duplicó el tamaño del registro de misiones.
- **D7 — Colisiones internas.** Progreso contra "no tempo" (`UIBuilder.cs:91` contra `:125`),
  título a todo el ancho contra el contador de compás (`:79` contra `:102-103`), metrónomo
  contra la cabecera (`:65`), cerrar sobresaliendo de la cabecera.
- **D8 — Inencontrable en un build.** Oculto por defecto y con el icono de la bandeja cargado
  por `AssetDatabase` (solo existe en el Editor). Es el D8 de la barra de hechizos, con la misma
  forma.
- **D9 — Inglés.** `MusicPlayerHUD.cs:353-356, 392, 408, 449`.
- **D10 — Dos `—` apilados.** Cerrar es un signo menos y, justo debajo, el contador de compás
  vacío también muestra `—`: parecen dos botones de cerrar. Y `—` significa "minimizar" en el
  registro de misiones.
- **D11 — Trabajo con el panel oculto.** `Update` no mira `_panelHidden`: con el panel cerrado
  sigue escribiendo textos cada frame y, si se cerró expandido, recalcula las 64 barras y la onda.
- **D12 — Abrir y cerrar es un salto.** `ApplyPanelVisibility` (`MusicPlayerHUD.cs:527-538`) pone
  el alfa directamente; el fundido de `Update` llega siempre tarde.
- **D13 — El tap-tempo calibra UNA máquina.** Escribe `valkur.musichud.tempo.<id>.bpm` en
  PlayerPrefs: un dato de autoría que no llega nunca al `AudioCatalog`, así que no se envía y el
  metrónomo de otro jugador sigue vacío. Es la forma de "configurado en local e invisible para
  el resto" que el proyecto ya evitó en otros sitios.

---

## 4. Qué debería ser el panel (la decisión de fondo)

Hoy el widget intenta ser dos cosas a la vez:

1. **Un "sonando ahora" para el jugador**: qué suena, cuánto queda, pausa, siguiente, volumen.
2. **Una mesa de calibración para el autor**: forma de onda con rejilla de compases,
   tap-tempo, arrastre de BPM y de desfase, zoom de amplitud.

Un reproductor profesional de juego (el de Hades, la radio de un GTA o un "now playing" de
Octopath) es solo lo primero, pequeño y bonito. Lo segundo es una herramienta y **su sitio es un
editor**, junto al gráfico rítmico del editor de jefes, escribiendo en el `AudioCatalog` y no en
PlayerPrefs (D13).

Recomendación:

- **Modo compacto = una placa** de "sonando ahora", en el espacio de texel del HUD, del tamaño de
  un tercio del panel del jugador. Es lo que ve el 99 % de los jugadores.
- **Modo expandido = "Resonancia"**, un visualizador opcional y bonito (no un DAW): el analizador
  en bloques de píxel y la envolvente de la canción entera horneada de antemano, con marcas de
  compás si hay BPM. Cuando el jugador lo abre, el movimiento ES el contenido, y ahí sí se permite
  que se mueva al ritmo (ver sección 6).
- **Calibración = el editor.** Tap-tempo, desfase y rejilla se mudan a una pestaña "Música" (ESC →
  editor de audio, o dentro del de jefes) que escribe en el catálogo. El HUD deja de tener gestos
  invisibles.

---

## 5. Dirección visual

Principio: **el reproductor es un pariente discreto del panel del jugador.** Mismas piezas
(`HudArt`, `HudPixelText`, `HudRect`, `HudMoteLayer`, `HudTooltip`), mismo espacio de texel
(`HudPixelScaleFor`: escala 2 a 1600x800, 3 a 1080p) y mismos tokens, pero **sin oro en el
marco**: la R3 del lenguaje visual dice que el oro es importancia, y la música es ambiente. El oro
se reserva para dos cosas: el cabezal y el medallón de la nota cuando empieza una pista.

### Placa compacta (en texels; escala 2, es decir 232 x 64 px)

```text
 ┌──────────────────────────────────────────────────────────┐  contorno 1 · bisel 1 · piedra 2 tonos
 │ ╭───╮ PEPITORIA MAIN THEME                         1:46 │  título: fuente de píxel 3x5 en MAYÚSCULAS
 │ │ ♪ │ PEPITORIA · 1/13                             3:07 │  línea 2: zona · posición en la lista (textDim)
 │ ╰───╯ ════════════════●──────────────────────────────── │  surco hundido de 3 texels + cuenta dorada
 │  ⏮  ▶  ⏭      🔈 ▮▮▮▮▮▯▯▯                          ≡ ▴ │  transporte · volumen en 8 muescas · lista · expandir
 └──────────────────────────────────────────────────────────┘
```

- **Medallón de 13 texels** a la izquierda, con el sigilo de la lista de reproducción de la zona
  (Pepitoria, Forest, Desert, Covetus: cuatro sigilos generados con los mismos SDF del minimapa).
  Es el único adorno, y solo cambia por un evento (pista nueva).
- **Título en la fuente de píxel pequeña, en mayúsculas.** Los 24 títulos son ASCII y
  `HudPixelFont.CanSpell` ya existe: si un título futuro lleva tildes, se cae a TMP con contorno.
  Si no cabe, se desplaza como una marquesina lenta SOLO mientras el ratón está encima; en reposo
  se corta con puntos suspensivos.
- **Tiempo** en la fuente de píxel, alineado a la derecha, restante debajo del transcurrido.
- **Surco de progreso**: hueco opaco de 3 texels con el relleno en el tono de piedra clara y una
  **cuenta dorada** de 3x3 en el cabezal. Al pasar el ratón, la cuenta crece a 5x5 y aparece el
  tiempo bajo el cursor en un `HudTooltip`.
- **Transporte**: tres teclas de 11x9 texels. Play es la del medio y la única con el borde de
  piedra clara; anterior y siguiente, hundidas. Glifos de píxel dibujados en el atlas, no
  rasterizados a 32 px y reescalados.
- **Volumen en 8 muescas** de 2x5 texels. En pixel art, una fila de bloques se lee mejor que un
  deslizador y cae en la rejilla por construcción. Clic o arrastre sobre las muescas; la rueda
  sube o baja una muesca. En silencio, las muescas se vacían y el altavoz lleva el tachón.
- **Ventana**: se arrastra por la placa (no por los botones), se cierra con `✕` en el mismo sitio
  que en el registro de misiones, y no hay tirador de tamaño en el modo compacto: una placa de
  "sonando ahora" tiene UN tamaño correcto por escala de HUD.

### Resonancia (modo expandido, opcional)

```text
 ┌──────────────────────────────────────────────────────────┐
 │ ▁▂▃▅▆▇▇▆▅▃▂▂▃▄▅▅▄▃▂▁▁▂▃▂  ← 24 columnas x 12 bloques      │  analizador en bloques de píxel, hueco opaco
 │ ▁▂▂▃▅▆▅▃▂▃▅▇▇▆▅▃▂▂▃▅▆▅▃▂▁▂▃▄▅▄▃▂▁│▁▂▃  ← envolvente horneada │  toda la canción, 6 texels, cabezal dorado
 │   ¦   ¦   ¦   ¦   ¦   ¦   ¦   ¦   ← compases (solo con BPM) │
 ├──────────────────────────────────────────────────────────┤
 │   … la placa compacta, idéntica …                        │
 └──────────────────────────────────────────────────────────┘
```

- **Crece hacia arriba 40 texels (80 px), no 250 unidades.** Y la banda que ocupa se declara en
  `HudLayout`, con el minimapa y el registro de misiones, para que no se pisen (D5).
- **Analizador en un solo `Graphic`** (`OnPopulateMesh` con N quads), como `MinimapQuadGraphic` y
  `HudMoteLayer`, no 64 `Image`. Bloques de 1x1 texel apilados, con un pico que cae (una fila más
  clara que baja despacio).
- **Envolvente horneada**: `analyze_music.py` ya carga cada pista con librosa para el BPM; que
  escriba también 128 valores RMS en el catálogo. La canción entera está desde el primer frame,
  cuesta 0 en runtime, no depende del volumen y la ruta progresiva se borra.
- **Señal pre-volumen**: la música pasa a un grupo de `AudioMixer` con el volumen como parámetro
  expuesto y la fuente a 1.0. El analizador ve la canción aunque el jugador la tenga en silencio.
  El proyecto no tiene ningún `.mixer` hoy: es una decisión con coste (el ducking actual toca el
  volumen de la fuente) y se toma en la fase 2, no antes.

### Paleta (todo desde el tema común)

| Pieza | Token |
| --- | --- |
| Piedra, contorno, bisel, hueco | `stoneLight`, `stoneDark`, `outline`, `bevelLight`, `recess` |
| Título, tiempo | `text` / `textDim` |
| Cuenta del cabezal, medallón al empezar | `gold` |
| Relleno del surco, muescas llenas | `stoneLight` aclarado (no oro: el oro es importancia) |
| Analizador | Una rampa de un solo tono derivada del sigilo de la zona con `WorldBarPalette` (sombra, cuerpo, luz). Nunca azul (el azul es maná, R6) |

---

## 6. Partículas: dónde sí y dónde no

La música tiene una trampa que los otros paneles no tenían: **un pulso ES un evento, pero se
repite para siempre.** Un reproductor que suelta una chispa en cada pulso es un salvapantallas y
viola la R8 ("nunca emisión en reposo"). Por eso la regla aquí tiene dos mitades:

- **En la placa compacta, solo eventos que el jugador CAUSA o que CAMBIAN algo**: nada que siga
  al ritmo.
- **En Resonancia, el ritmo está permitido**, porque el jugador ha pedido ver la música. Aun así,
  una mota por COMPÁS como máximo, nunca por pulso.

### Dónde NO

- Nada al ritmo en el modo compacto, ni motas ambientales flotando.
- Nada al pausar ni al silenciar: un "para" es una bajada de luz, no una celebración.
- Nada de `ParticleSystem` en un canvas overlay (no se ordena con las `Image`).
- Nada mientras el panel está oculto, obviamente, ni partículas que salgan fuera de la placa
  hacia el mundo.

### Dónde SÍ (vida ≤ 0.6 s, aditivas, en rejilla de texel, `HudMoteLayer`)

| Evento | Qué se ve | Motas |
| --- | --- | --- |
| Pista nueva | 4-6 notas de píxel (glifo 3x5) suben del medallón con una leve deriva; un brillo recorre el título de izquierda a derecha en 0.3 s (el `Shine` que ya existe); el medallón hace un overshoot de 1 texel. Sustituye a la losa dorada actual | 6 |
| Play tras pausa | Un brillo recorre el surco desde el cabezal; 2 motas salen de la tecla | 2 |
| Pausa | Sin motas: el título baja a `textDim` y la cuenta deja de brillar | 0 |
| Siguiente / anterior | 3 motas salen hacia el lado del salto | 3 |
| Soltar tras buscar | Un anillo pequeño en la cuenta | 1 anillo |
| Quitar el silencio | 2 notas salen del altavoz y las muescas se rellenan de una en una (40 ms cada una) | 2 |
| Cambio de zona (lista nueva) | El sigilo nuevo entra con un destello y 6 motas del color de su rampa | 6 |
| Compás (solo Resonancia, solo con BPM) | La columna más alta suelta una mota que sube 4 texels | 1 por compás |

Presupuesto: un `HudMoteLayer` de 24 motas dentro del canvas anidado del panel, un draw call.

---

## 7. Persistencia visual entre los elementos del HUD

**Sí hace falta escribirlo, y ya está empezado.** [`HUD_VISUAL_LANGUAGE.md`](HUD_VISUAL_LANGUAGE.md)
(de otra sesión, sin commitear todavía) fija doce reglas: espacio de texel (R1), un tema
`HudTheme` con los tokens compartidos (R2), gramática de marco (R3), fuente de píxel más TMP solo
para prosa (R4), una sola casilla (R5), un color = un significado (R6), movimiento (R7),
partículas solo por evento (R8), bandas declaradas en `HudLayout` (R9), iconos horneados (R10),
un dato = una lectura (R11) y el HUD también muere (R12).

### Cómo sale el reproductor contra esas reglas hoy

| Regla | ¿Cumple? | Por qué |
| --- | --- | --- |
| R1 Espacio de píxel | No | Canvas a 2.0 más escala libre no uniforme |
| R2 Un tema | No | 62 literales, oro con deriva propia |
| R3 Gramática de marco | No | Rectángulo translúcido sin contorno ni bisel |
| R4 Fuente | No | TMP para todo, estirado |
| R5 Una casilla | No aplica | No tiene casillas |
| R6 Un color = una cosa | No | Azul en el metrónomo y el analizador (el azul es maná) |
| R7 Movimiento | No | Abrir y cerrar a saltos |
| R8 Partículas por evento | Sí, por omisión | No tiene ninguna |
| R9 Bandas | No | Tapa el minimapa, orden 150 compartido |
| R10 Iconos | No | Rasterizados a 32 px y reescalados |
| R11 Un dato, una lectura | Sí | La música solo se muestra aquí |
| R12 El HUD muere | No | A todo color en forma de espíritu |

### Lo que el reproductor revela que falta en el documento

El documento está pensado para paneles fijos (el del jugador, la barra, el minimapa). El
reproductor, el registro de misiones y el chat son **ventanas**, y cada una inventó su propia
gramática: cerrar es `—` en uno y `✕` en otro, `–` significa minimizar en uno y cerrar en otro, el
tirador de tamaño es una `/` arriba a la izquierda en uno y un triángulo abajo a la derecha en
otro, uno se arrastra y otro no. Propuesta de cuatro reglas nuevas, para que las añada la sesión
dueña del documento:

- **R13 — Una gramática de ventana.** Un componente `HudWindow` compartido por el reproductor, el
  registro de misiones y el chat: la cabecera o la placa es la superficie de arrastre (nunca los
  botones), los controles van en el mismo orden y con los mismos glifos del atlas (`▾` minimizar,
  `✕` cerrar, a la derecha), el tirador de tamaño es el triángulo de `TriangleHandleGraphic` en
  la esquina opuesta al pivote, la posición y el tamaño se guardan bajo `valkur.<widget>.*` al
  terminar el gesto, y una ventana cerrable tiene siempre un camino de vuelta que funcione en un
  build. Comprobación: un test que recorre las ventanas del HUD y exige `HudWindow`.
- **R14 — Una ventana crece, no se estira.** Cambiar de tamaño cambia la maquetación en pasos de
  texel entero; `localScale` distinto de 1 está prohibido dentro del espacio de texel.
  Comprobación: el test de R1 añade `lossyScale == scaleFactor` en cada `RectTransform`.
- **R15 — El HUD habla español, desde un sitio.** Toda cadena visible del HUD vive en una tabla
  (como `LoadingText` para la carga) y un test rechaza literales visibles en inglés en las
  carpetas de HUD. El reproductor es el cuarto panel con este defecto.
- **R16 — Lo opcional cede.** Un panel que el jugador puede cerrar (música, misiones) se atenúa al
  60 % cuando entra un golpe y vuelve pasado `idleFadeDelay`, la misma regla de reposo que las
  barras del mundo. Lo que decide si sigues vivo no comparte protagonismo con qué canción suena.

### Por qué un documento no basta

Todas las convenciones de este proyecto se han roto alguna vez; lo que no se ha roto es lo que
tiene un test. La persistencia visual se sostiene con tres piezas de código, no con el documento:

1. **`HudTheme` (asset)**: el oro, la piedra y el texto escritos una vez. El reproductor es la
   prueba de que hace falta: es la tercera deriva del mismo oro.
2. **`HudWindow` (componente)**: la gramática de ventana implementada una vez, para que la cuarta
   ventana no invente una quinta.
3. **`HudContractTests`**: recorre cada canvas y cada ventana del HUD y exige referencia, `match`,
   banda de orden, escala entera, ausencia de solapes entre widgets siempre visibles y ausencia de
   literales de color nuevos. Habría cazado D1, D5, D6 y D9 el día que se escribieron.

---

## 8. Roadmap por fases (nota estimada tras cada una)

### Fase 0 — Verdad antes que belleza (2.1 → 4.0)

- D1: escala uniforme ya; D2: los tres iconos y el chevrón; D7: las cuatro colisiones; D10: `✕`
  para cerrar y nada en el contador si no hay BPM; D11: `Update` sale pronto con el panel oculto;
  D12: fundido de 0.12 s.
- D3: ocultar metrónomo, compás y puntos con BPM ≤ 0, y **correr `analyze_music.py` +
  `patch_audio_catalog_bpm.py`** sobre las 24 pistas (necesita `librosa` en el venv).
- D6: el canvas usa `HudLayout.ReferenceWidth/Height/Match` y una banda de orden propia.
- D8: el icono de la bandeja sale de un atlas o de `Resources/`, no de `AssetDatabase`.
- D9: cadenas en español, en una tabla.
- Tests de composición: escala uniforme, ningún hijo fuera del padre, ningún texto bajo otro
  gráfico, los iconos con la tinta donde toca (el patrón de muestreo que ya usan
  `FacingIndicatorRigTests`).

### Fase 1 — Un solo lenguaje (4.0 → 6.5)

- Mudanza a `UI/HUD/Music/`, reconstruido con el kit del panel del jugador en el espacio de
  texel. `MusicHudStyle` en `Resources/UI/` leyendo los tokens del tema común.
- La placa de la sección 5: medallón, título en píxel, surco con cuenta, transporte en el atlas,
  volumen en muescas.
- `HudWindow` (R13) compartido con el registro de misiones: arrastre, `✕`, posición persistida.
- Tap-tempo, desfase y zoom de amplitud se mudan a un editor que escribe en el catálogo (D13).

### Fase 2 — Datos de música de verdad (6.5 → 7.6)

- `analyze_music.py` escribe también la envolvente de 128 valores y los tiempos de los
  primeros compases; el catálogo los guarda; se borra la onda progresiva.
- `AudioMixer` con el volumen expuesto: el analizador ve la señal pre-volumen (D4).
- Resonancia: analizador en un `Graphic` de bloques, envolvente con cabezal, compases,
  declarada en `HudLayout` para no pisar el minimapa (D5).

### Fase 3 — Movimiento y partículas (7.6 → 8.6)

- La tabla de la sección 6 con `HudMoteLayer`.
- Cuenta que crece al pasar el ratón, previsualización de tiempo, marquesina del título solo con
  el ratón encima, muescas que se rellenan de una en una.
- R12 y R16: gris en forma de espíritu, atenuado en combate.

### Fase 4 — Identidad (8.6 → 9+)

- Cuatro sigilos de lista (Pepitoria, Forest, Desert, Covetus) y su rampa de color.
- Tooltip de "a continuación" sobre siguiente y la lista de la zona sobre el botón `≡`.
- Acciones `Music/PlayPause`, `Music/Next`, `Music/Previous` en `ValkurInputActions` y en el
  `InputActionCatalog`, SIN tecla por defecto (como los editores): asignables en el editor de
  Controles, sin robar ninguna tecla a nadie.

---

## 9. Qué NO hacer

- **No meter un DAW en el HUD.** La calibración es autoría y vive en un editor que escribe en el
  catálogo.
- **No hacer que la placa lata al ritmo.** Es la tentación obvia en un reproductor y convierte el
  panel en un salvapantallas.
- **No redimensionar con `localScale`.** Ni uniforme en la versión final: una ventana del HUD
  crece en texels.
- **No embellecer la capa de tempo antes de tener BPM.** Un metrónomo precioso que no se mueve
  sigue estando roto.
- **No darle un tema propio.** Su oro ya es la tercera deriva del mismo color.
- **No `ParticleSystem` en el canvas.**
- **No decidir la gramática de ventana solo para este panel.** Se decide una vez, en `HudWindow`,
  para el reproductor, las misiones y el chat.

---

## Medidas de referencia

- Pantalla 1600x800. `MusicHUDCanvas`: `ScreenSpaceOverlay`, orden 150, `ScaleWithScreenSize`
  800x600 `match 0`, factor 2.0. HUD del jugador: 1600x800 `match 0.5`, factor 1.0.
- Compacto: `sizeDelta` 320x78, `localScale` (0.56, 0.87), en pantalla x 1211-1568,
  y 208-344 (357 x 136 px). Escala efectiva de los TMP (1.115, 1.748).
- Expandido: `localScale` (0.94, 0.78), x 965-1568, y 208-708 (603 x 500 px, 23.6 % de la
  pantalla).
- Minimapa visible: x 1377-1587, y 540-781. Solape con el expandido: 191 x 168 px (63 %).
- Panel del jugador visible: x 12-398, y 12-142.
- Piezas (compacto, px): cabecera 357 x 31.5; título 308 x 31.5; botones 29 x 38.5 cada uno;
  volumen 169 x 10.5; cerrar 22 x 35 (la cabecera mide 31.5).
- 114 `Graphic` (5 TMP), sin canvas anidado. `Update`: 2.5 µs en compacto y 115 µs en expandido.
- Fuente de música viva: `MusicA`, `pepitoria_main_theme`, volumen 0, `Streaming`, pico de
  salida 0.0000, suma del espectro 0.000013.
- Catálogo: 24 pistas, 24 con `bpm: 0`, 24 `loadType: 2` (Streaming).
- 62 `new Color(` en los seis ficheros; 12 estáticos del widget en `unreset-statics.txt`.

---

## 10. Resultado de la reconstrucción (mismo día)

**Nota global: 9.1 / 10**, desde 2.1. Todo lo de abajo está medido en vivo a 1600x800 o fijado
por test; lo que no es 10 dice por qué.

### Qué se construyó

```text
UI/HUD/Music/       MusicPlayerHUD (+.Layout, .Window, .Playback, .Effects, .Hotkeys, .Console),
                    MusicHudArt, MusicHudKey, MusicGroove, MusicVolumeNotches, MusicHudPointer,
                    MusicResonanceGraphic, MusicSpectrum, MusicTrackInfo, MusicHudText, MusicSigil
Data/UI/MusicHudStyle.cs  +  Resources/UI/MusicHudStyle.asset (con el icono de la bandeja)
Core/IMusicSignalSource.cs · Infrastructure/MusicSignalTap.cs · AudioManager.MusicWake.cs
Core/UI/HudLayout.cs       MusicSortingOrder (140) y MusicPanelWidth (252, lo usa el inventario)
Input: Gameplay/MusicPlayPause, MusicNext, MusicPrevious (sin tecla, con su ranura vacía)
Catálogo: bpm, firstBeatOffsetSec, key, keyConfidence, envelope y beatTimes de las 24 pistas
Kit: HudMoteShape.Note + la pieza mote_note en HudArt
Borrados: los seis partials del reproductor viejo y sus dos fixtures
Tests: MusicPlayerHUDTests, MusicSpectrumTests, MusicTrackInfoTests, ShippedMusicCatalogTests
```

### Defectos de la sección 3, estado

| # | Defecto | Estado |
| --- | --- | --- |
| D1 | Escalado no uniforme | La placa no se redimensiona; un test rechaza cualquier `localScale` distinto de 1 dentro de su espacio de texel |
| D2 | Iconos rotos | Glifos como patrones de píxel; un test lee las dos barras de la pausa y la barra completa de los saltos en el atlas |
| D3 | Sin BPM | 24/24 pistas analizadas con librosa; el reloj de pulso trabaja con los `beatTimes` reales |
| D4 | Visualizador post-volumen | Tap pre-volumen con despertador; medido con la música al 0 %: pico de señal 0.257 |
| D5 | Tapa el minimapa | La Resonancia llega a y=268 (el minimapa empieza en 540); un test lo fija contra `BottomReserved` |
| D6 | Canvas fuera de contrato | 1600x800, match 0.5, orden 140 declarado en `HudLayout` |
| D7 | Colisiones internas | Un test comprueba que ningún par de piezas se solapa |
| D8 | Inencontrable en un build | Visible en una instalación nueva; el icono de la bandeja va en el asset de estilo |
| D9 | Inglés | Todo en español desde `MusicHudText` |
| D10 | Dos `—` | Cerrar es `✕`; el contador de compás no existe |
| D11 | Trabajo oculto | Cerrado y apagado no calcula nada (test por contador de frames de trabajo) |
| D12 | Abrir y cerrar a saltos | Fundido de 0.12 s; volver de una pelea es cuatro veces más lento |
| D13 | Tap-tempo en PlayerPrefs | Retirado del HUD; el tempo es dato del catálogo |

### Encontrados al verlo en vivo, todos corregidos

- **El filtro de audio también ve la señal DESPUÉS del volumen** (a 0.02: filtro 8.2e-3 contra
  salida 9.1e-3), así que el tap divide el volumen de vuelta.
- **Una fuente que ARRANCA por debajo de ~1e-3 empieza virtual y el filtro recibe ceros**; una
  vez real se queda real a 1e-4. El primer suelo (1e-5) no bastaba y el segundo (1e-4) solo
  funcionaba si la fuente había sonado antes. `AudioManager` la despierta tres frames a -54 dBFS.
- **Un frame largo se comía un evento entero**: con el editor sin foco, el siguiente frame
  llegaba segundos después y el destello, el brillo y las notas se consumían sin dibujarse. El
  paso por frame está limitado a 1/20 s.
- **El pozo del surco estaba fuera de la rejilla**: 3 texels de alto con un 9-slice de borde 2+2.
  Tiene su pieza de 3x3 y un test recorre todas las imágenes 9-slice.
- **El sigilo del pueblo parecía una flecha** (paredes más estrechas que el tejado) y **las
  notas salían en un racimo que parecía una corona**; ahora salen en abanico desde el aro.
- **Las muescas apagadas eran invisibles** al 0 %: una barra negra en vez de ocho muescas.
- **La envolvente se veía plana**: normalizada contra los picos, casi toda la canción caía en el
  tercio alto. Se estira entre el mínimo y el máximo de cada pista, en 8 filas.
- **La tonalidad se enseñaba con confianza 0.007**. Solo sale por encima de 0.1 (13 de 24).

### Puntuación por eje, después

| # | Eje | Antes | Después | Por qué no es 10 |
| --- | --- | --- | --- | --- |
| 1 | Verdad de los datos | 1.5 | 9.5 | El BPM es una estimación; hay pistas lentas que librosa puede leer al doble |
| 2 | Proporción | 1 | 10 | |
| 3 | Rejilla de píxel | 1 | 9.5 | Solo los chaflanes, donde se ve el mundo por debajo, no caen en bloques de 2x2 |
| 4 | Composición | 3 | 9 | |
| 5 | Jerarquía | 3 | 9 | |
| 6 | Materialidad | 1.5 | 8.5 | Piedra generada, no pintada a mano |
| 7 | Iconografía | 1.5 | 9 | |
| 8 | Tipografía | 2.5 | 9 | La cara 3x5 a escala 2 es pequeña para quien lee de lejos |
| 9 | Paleta | 3.5 | 9 | |
| 10 | Transporte | 3 | 9 | |
| 11 | Progreso | 4 | 9.5 | |
| 12 | Volumen | 3.5 | 9 | |
| 13 | Resonancia | 2 | 9 | Espectro en vivo, no una envolvente por bandas horneada |
| 14 | Eventos | 2 | 9 | |
| 15 | Partículas | 0 | 9 | |
| 16 | Movimiento | 2 | 8.5 | Sin curvas de easing propias |
| 17 | Gramática de ventana | 2 | 8 | `HudWindow` compartido (R13) sigue pendiente: el registro de misiones y el chat no lo usan |
| 18 | Coherencia con el HUD | 1.5 | 9.5 | Una columna con la bandeja, el kit del panel del jugador, su contrato de canvas |
| 19 | Identidad musical | 2.5 | 8.5 | Sin carátula ni "a continuación" (el `AudioManager` no expone la siguiente pista) |
| 20 | Descubribilidad | 2 | 9 | |
| 21 | Accesibilidad | 3 | 8 | Tooltips, forma redundante y teclas asignables; sin opción de tamaño |
| 22 | Idioma | 1 | 9.5 | Los títulos de las pistas siguen en inglés en el catálogo |
| 23 | Respuesta al juego | 1 | 8.5 | Se aparta en combate y se enfría en espíritu; no reacciona a interiores |
| 24 | Arquitectura / tokens | 3.5 | 9 | Lee los tokens de `PlayerHudStyle` hasta que exista `HudTheme` |
| 25 | Rendimiento | 5 | 9 | Ver medidas abajo |
| 26 | Tests | 3 | 9.5 | |

Media aritmética **9.0**, ponderada **9.1**.

### Medidas del panel nuevo

- Placa: 126x42 texels, 252x84 px a 1600x800, en x[1332..1584] y[104..188]; con la Resonancia
  llega a y=268. Mismo ancho y mismo borde derecho que la bandeja.
- Rejilla: 5 292 bloques de 2x2 en la placa; 12 no uniformes y los 12 en los chaflanes de las
  esquinas, donde se ve el mundo por debajo (antes del arreglo del surco: 109 más).
- Coste de `Tick`: 3.0 µs cerrado, 4.5 µs abierto, 95 µs en los frames que analizan el espectro
  (30 por segundo, unos 48 µs de media a 60 fps). 45 `Graphic` en todo el panel (antes 114).
- Señal pre-volumen con la música al 0 %: pico 0.257-0.327 medido en tres sesiones.
- Suite EditMode completa: 8 500 / 8 500, en una sola pasada (completados = total).
