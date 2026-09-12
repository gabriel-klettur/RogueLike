# Auditoría de belleza de la pestaña SKILLS (SkillTreeHUD)

**Fecha:** 2026-09-12 · **Nota global: 1.8 / 10** · Objetivo: **≥ 8.5**

Alcance: la pestaña **SKILLS** de la hoja de personaje — `Gameplay/HUD/SkillTreeHUD.cs`
(293 líneas, un solo fichero) y el cromo que la aloja,
`UI/HUD/CharacterSheetController.cs` y `.UIBuilder.cs`. Se citan por contraste sus tres
hermanas (`CharacterStatsHUD`,
`SpellTreeHUD`, `StatisticsHUD`), que comparten el mismo rect, la misma fuente y los mismos
defectos, y las cinco superficies ya reconstruidas contra el contrato: el panel del jugador
(8.9), el reproductor de música (9.1), el inventario, el minimapa y la barra de acciones (8.2).

Método: lectura completa del código y de los 40 assets de `Data/Progression/SkillTrees/`,
medidas en vivo en Play Mode a 1600x800 vía `execute_code` (rects en píxeles de pantalla,
`scaleFactor` de las 36 canvas, `characterCountVisible` de cada etiqueta) y dos capturas con
`ScreenCapture.CaptureScreenshot` congeladas con `Time.timeScale = 0` —una con el panel
abierto y otra cerrado— para medir por diferencia cuánto del mundo atraviesa la placa. Cada
nota va con su evidencia.

El lenguaje visual que la sección 7 da por supuesto está en
[`HUD_VISUAL_LANGUAGE.md`](HUD_VISUAL_LANGUAGE.md); esta pestaña es el punto 4 de su orden de
aplicación (sección 5.5: "chat y misiones; después crafteo, **árboles**, estadísticas y pausa").

**Documento hermano:** [`GRIMOIRE_BEAUTY_AUDIT_2026-09-12.md`](GRIMOIRE_BEAUTY_AUDIT_2026-09-12.md)
audita la pestaña GRIMOIRE el mismo día y llega a la misma conclusión desde el otro lado: las
dos comparten cromo, canvas, fuente y defectos, y **deben reconstruirse en la misma pasada**.
Lo que no comparten es el layout —siete nodos en 3x3 contra 73 hechizos en 9 escuelas— y eso
está argumentado al final de la sección 4.

---

## 1. Resumen

El panel no tiene un problema de belleza. Tiene tres, y solo el tercero es de belleza.

**Primero, no dice la verdad.** Las siete filas están truncadas —las siete, medido— y de media
se tira **el 52 % de cada frase**. La razón de bloqueo que imprime es la equivocada: Bulwark
dice `Need 2 skill point(s)` cuando lo que de verdad lo cierra es que Stoneflesh no está al
rango máximo. Y el panel es **ciego al único evento para el que existe**: subir de nivel llama
a `LearnedSkills.AddPoints`, que dispara `OnPointsChanged`; el panel solo escucha
`OnLoadoutChanged`. Sube de nivel con la pestaña abierta y la cabecera sigue diciendo
`0 point(s) available` hasta que la cierras y la vuelves a abrir.

**Segundo, tira el diseño que ya existe.** Los cinco árboles están autorados como grafos reales
de 3-3-1: tres raíces en la fila 0, tres nodos intermedios en la fila 1 cada uno con su
prerrequisito, y un capstone en la fila 2. `SkillNode.row` y `.column` están puestos a mano en
los 35 assets. El panel usa esos dos campos **como clave de ordenación de una lista plana** y
tira la forma. También tira `SkillNode.icon` (35 de 35 sin arte, y ningún lector en todo el
proyecto), `SkillNode.description` (autorada en los 35: "Years underground thicken more than the
skin.") y `SkillTree.flavour` ("Stone does not dodge. It simply refuses to move.").

**Tercero, está vestido de prototipo, y su propio comentario lo dice**: *"Genuine production UI
would lay out the graph spatially… designers replace it with a graph layout later via prefab."*
Es la única superficie del HUD que dibuja con `UnityEngine.UI.Text` y `LegacyRuntime.ttf` —una
**tercera** familia tipográfica, además de la bitmap del panel del jugador y la TMP del resto—,
su canvas escala a **factor 2.000** contra el 1.000 de todo lo demás, así que cada letra se
dibuja al doble de tamaño que el juego que hay debajo, y su placa deja pasar el **38 % del valor
sRGB del mundo**, con lo que el propio fondo del panel varía **×3.04 en luminancia** según dónde
esté el jugador.

---

## 2. Puntuación por eje

| # | Eje | Nota | Evidencia medida |
| --- | --- | --- | --- |
| 1 | Verdad del texto | 0 | **7 de 7 filas truncadas.** Medido con `Text.cachedTextGenerator.characterCountVisible`: 42/86, 47/80, 43/95, 45/83, 44/98, 42/97, **40/117**. Media 44 de 92 caracteres = **52 % perdido**. La etiqueta tiene 290 px de rect contra 536–768 de `preferredWidth` |
| 2 | Verdad de la razón de bloqueo | 0.5 | `LearnedSkills.CanLearn` prueba la ASEQUIBILIDAD antes que los PRERREQUISITOS, así que con 0 puntos los siete nodos dicen `Need N skill point(s)`. El gate real de Bulwark es Stoneflesh al rango 5 (5 puntos, no 2) y el panel nunca lo nombra |
| 3 | Reactividad a los eventos | 0 | `AddPoints` dispara solo `OnPointsChanged`; `SkillTreeHUD` solo se suscribe a `OnLoadoutChanged`. Subir de nivel o cobrar una misión (`QuestManager.cs:457`) no repinta nada. `playerLevel` se cachea en `BindLearnedSkills` y no se vuelve a leer nunca: un gate de nivel se queda obsoleto igual |
| 4 | Forma del árbol | 0 | Los 35 nodos autoran `row`/`column` en una rejilla 3x3 con capstone. `Refresh()` los usa como `Sort` y dibuja siete filas iguales. Cero aristas, cero jerarquía, cero columnas. La única pista de estructura es la palabra `Locked` |
| 5 | Iconografía | 0 | 35 de 35 assets con `icon: {fileID: 0}`. `SkillNode.icon` no tiene ni un lector en el proyecto. El comentario de cabecera del panel promete `[icon] Display Name (cost)` y nunca dibuja uno |
| 6 | Partículas | 0 | Ninguna. `HudMoteLayer` existe en UIKit y ya la usan cinco superficies (panel del jugador, música, inventario, barra de acciones, HUD de depuración). Aprender un rango —el único acto del panel— no produce un píxel |
| 7 | Escala de píxel | 0.5 | Medido en vivo: `SkillTreeHUD_Root` **factor 2.000**, `ref = 800x600`, `match = 0`. Todo el HUD sano va a 1.000 con `ref = 1600x800`, `match = 0.5`. Es el defecto exacto que `HudLayout` se creó para matar (y que ya reventó la fuente del registro de misiones). Las cinco canvas de la hoja lo tienen |
| 8 | Materialidad de la placa | 1 | `Image` sin sprite a `(0, 0, 0, 0.85)`. Medido por diferencia de dos capturas congeladas: transmitancia lineal 0.143, pero **sobrevive el 38 % del valor sRGB del fondo**; la luminancia de la placa varía **×3.04** de un punto a otro del panel y correlaciona 0.534 con el mundo de detrás. No es una superficie, es un filtro |
| 9 | Contraste | 2 | El blanco sobre la placa oscila de **21.0:1** a **6.9:1** según lo que haya debajo. Legible por suerte, no por diseño: basta abrir el panel sobre nieve o sobre el altar iluminado para perder el efecto en gris |
| 10 | Tipografía | 0.5 | `UnityEngine.UI.Text` + `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")`, a 14 pt, escalado x2 = 28 px efectivos. Solo cuatro ficheros del proyecto hacen esto y los cuatro son las pestañas de esta hoja. Una TERCERA familia frente a las dos que `HUD_VISUAL_LANGUAGE` ya cuenta como defecto |
| 11 | Idioma | 1 | `Learn`, `Available`, `Locked`, `point(s) available`, `Path of the Mountain`, `Stoneflesh`, `Iron Hide`… en inglés, y en la misma frase "Vida máxima", "Daño cuerpo a cuerpo" en español porque `StatCatalog.DisplayName` sí está traducido. Y la mitad española es justo la que se trunca. Viola R15 |
| 12 | Desbordamiento | 0.5 | Siete filas de 64 px + 6 huecos de 8 = **496 px contra una lista de 476**. Sin `ScrollRect`. La última fila sobresale 20 px. **Siete nodos es el techo de este panel y los cinco árboles tienen exactamente siete**: un nodo más y desaparece sin avisar |
| 13 | Densidad y aprovechamiento | 1 | La etiqueta ocupa el 67 % del ancho de la fila y el botón el 26 %; cuando no hay botón —que es siempre que no se puede aprender— **un tercio de cada fila es vacío** mientras el texto de al lado se corta. Fila de 64 px de alto para una línea de texto de 24 |
| 14 | Jerarquía visual | 1 | Siete rectángulos idénticos a `(0.10, 0.10, 0.12, 0.85)`. Nada distingue una raíz de un capstone, un nodo de 5 rangos de uno de 1, ni lo comprado de lo disponible salvo la palabra |
| 15 | Semántica del color | 1 | El único color del panel es el botón Learn a `(0.30, 0.55, 1.0)`: **azul**, que en R6 significa maná y "te falta maná". El oro —que en R6 significa importancia— no aparece en ninguna parte salvo el subrayado de la pestaña activa. Un árbol de talentos entero sin un solo color con significado |
| 16 | Estados del nodo | 1 | Dos: con botón y sin botón. Faltan comprado-parcial, comprado-al-máximo, disponible, bloqueado-por-puntos, bloqueado-por-nivel, bloqueado-por-prerrequisito y el rango como pips. `[0/5]` en texto es el único indicador de rango y compite con la frase que lo trunca |
| 17 | Interacción | 1.5 | Un clic en un botón. Sin hover, sin tooltip, sin clic derecho para desaprender, sin previsualizar "qué me da el rango siguiente", sin arrastrar, sin teclado. `LearnedSkills.Respec()` existe en el modelo y **no tiene botón**: solo la consola |
| 18 | Movimiento | 0 | `Refresh()` destruye los siete hijos y los reconstruye. No hay fundido, ni easing, ni transición al abrir. Abrir y cerrar es `SetActive` |
| 19 | Composición y orden de dibujo | 1.5 | El rect centrado x[320..1280] y[120..680] es correcto. Pero el panel está a `sortingOrder` **60** y el `HUDCanvas` a 100: medido, `PlayerHUDPanel` y `ComboHUDPanel` solapan 1 716 y 4 680 px² **y se dibujan encima**. R13 dice que una ventana no se abre sobre un instrumento; esta se abre DEBAJO. La tira de pestañas va a 120, así que el minimapa (105), el reloj (105) y el chip de postura (105) caen entre la pestaña y su propio panel |
| 20 | Modalidad | 1 | No pausa, no oscurece, no toca `InputBlocker`. Los monstruos siguen pegando mientras el jugador decide en qué gastar un punto, y el mundo sigue animándose a través de la placa translúcida compitiendo con el texto |
| 21 | Descubribilidad | 2 | Sin tecla propia: se llega por doble clic en la fila de habilidades o por el verbo "Talentos" de la cara de Paz. Y **nada avisa de que hay un punto sin gastar**: `HUDIconBar.SetBadge` existe y solo la usa el inventario. El jugador sube de nivel y no se entera de que tiene una decisión pendiente |
| 22 | Nomenclatura | 2 | **ESC → Skills es el editor de OFICIOS** (cocina, herrería, minería) y la pestaña SKILLS son los TALENTOS. Dos cosas distintas con el mismo nombre en dos sitios que el jugador abre el mismo día |
| 23 | Accesibilidad | 2 | Estado solo por palabra, en inglés, truncada. Sin redundancia de forma. Sin escala. El contraste depende del suelo |
| 24 | Coste de repintado | 3 | Destruir y reconstruir siete filas es barato hoy. Es la misma forma que costó 3 480 ms al editor de objetos, y aquí se dispara en cada compra |

**Media ponderada: 1.8 / 10.** Los ejes 1-6 pesan doble: un panel bonito que miente es peor que
uno feo que no.

---

## 3. Defectos en orden de gravedad

1. **El panel no se entera de que has subido de nivel** (eje 3). `AddPoints` dispara
   `OnPointsChanged` y el panel escucha `OnLoadoutChanged`. Ninguno de los dos está mal por
   separado; es la composición la que falla, como el drift de los spawners y el bumerán. Y como
   el panel se abre normalmente DESPUÉS de subir de nivel, es invisible casi siempre: solo
   muerde a quien lo deja abierto, que es exactamente quien está decidiendo.
2. **El 52 % de cada frase se tira** (eje 1). No es un recorte estético: lo que se pierde es el
   efecto —"+80 Vida máxima, +5 Defensa, +15 % Daño cuerpo a cuerpo" del capstone, entero— y la
   mitad de la razón de bloqueo. Nada en la pantalla dice que falta texto.
3. **La razón de bloqueo es la equivocada** (eje 2). El orden de las comprobaciones en `CanLearn`
   es el inverso al que necesita un jugador: la falta de puntos es temporal y ya está en la
   cabecera; el prerrequisito es estructural y es la única respuesta accionable. Es el mismo
   argumento que la auditoría de misiones dejó escrito para `CraftingService` ("primero el
   NIVEL, el único rechazo que el jugador no puede arreglar andando").
4. **Siete nodos es el techo, y hay siete** (eje 12). Sin `ScrollRect`, la fila ocho no existe.
   Cualquier ampliación del árbol —que es lo natural— desaparece en silencio.
5. **Factor de escala 2.000** (eje 7). Cinco canvas de la hoja contra las 20 sanas del resto del
   HUD. Es la causa directa de que en la captura el texto parezca de otro juego, y se arregla
   con tres líneas por canvas.
6. **La placa es un filtro, no una superficie** (eje 8). 38 % del fondo sobrevive, ×3.04 de
   variación. El inventario ya pagó esta lección: "en espacio lineal, un 96 % opaco no es opaco".
7. **El grafo autorado se tira** (eje 4). Es el defecto más caro de arreglar y el que más cambia
   el panel, y **no necesita ni un byte de datos nuevos**.
8. **Cero iconos** (eje 5) y cero uso de `description` y `flavour`, que están escritos.
9. **Cero partículas y cero feedback** (ejes 6 y 18). Gastar un punto de talento es una decisión
   permanente y no produce ningún acontecimiento.
10. **Se dibuja debajo del HUD** (eje 19) y no pausa (eje 20).

---

## 4. Qué debería ser el panel (la decisión de fondo)

**Un árbol, con las aristas dibujadas, sobre un tablero que cabe entero en la pantalla.**

Los datos ya lo permiten hoy, y eso es lo que hace que esta sea una decisión y no un deseo. Los
cinco árboles tienen forma idéntica y pequeña:

```text
fila 0   [Stoneflesh 0/5]   [Iron Hide 0/5]   [Heavy Hands 0/4]   <- tres raices
               |                   |                  |
fila 1   [Bulwark 0/3]      [Anvil Stance 0/3] [Forge Heat 0/3]   <- un prerrequisito cada uno
                                   |
fila 2                       [Unbroken 0/1]                       <- capstone
```

Tres columnas y tres filas. Siete nodos. **Cabe entero, sin scroll, sin paginar y sin truncar
nada**, que es justo lo que la lista no consigue con los mismos siete nodos.

Lo que gana el jugador con la forma, y que hoy no tiene de ninguna manera:

- **Ve el coste real de un capstone antes de empezar.** Hoy `Unbroken — Locked: Need 3` no dice
  que por debajo hay 5 + 3 puntos de camino. Con las aristas dibujadas es un vistazo.
- **Ve que hay TRES caminos y que no puede tenerlos todos**, que es la pregunta que el árbol
  existe para plantear. En una lista ordenada por `row` los tres caminos están entrelazados.
- **Ve dónde ha invertido.** Una rama comprada se lee por el color de sus aristas.

Y lo que gana el proyecto: `SkillTree.TotalPointCost()` ya existe y no lo lee nadie; la forma es
lo que lo hace legible.

**Un nodo es la casilla de R5, no una fila.** `HudAbilitySlot` ya tiene cinco estados con el
mismo marco, la misma tecla desde el binding vivo y el mismo icono horneado, y es lo mismo que
necesita un talento con otros cinco estados. Eso obliga a una decisión de arquitectura que es la
parte barata y la que más rinde:

> **Mover `SkillTreeHUD` y `SpellTreeHUD` de `Valkur.Gameplay` a `Valkur.UI`.**
> `Valkur.UI → Valkur.Gameplay` **está permitido** (leído de los `.asmdef`: `Valkur.UI`
> referencia `Valkur.Gameplay`), y `CharacterSheetController` —que ya vive en `Valkur.UI`—
> importa `Valkur.Gameplay.HUD` para construirlos. La dirección prohibida es la contraria.
> Ningún fichero de Gameplay referencia a `SkillTreeHUD` fuera de comentarios, y el asmdef de
> los tests referencia las dos asambleas: el movimiento es un `git mv` y un `using`.
>
> A cambio, el panel gana `HudAbilitySlot`, `HudTooltip` y `HudPortrait` **sin duplicar
> nada** (`HudArt`, `HudMoteLayer`, `HudMedallion`, `HudRect`, `HudPixelFont` y
> `HudTextureBaker` ya viven en `Valkur.UIKit` y se alcanzan desde las dos asambleas; los
> tres primeros son justo los que se quedaron en `Valkur.UI`). El inventario no pudo hacer
> esto —es Gameplay de verdad, por
> `Inventory`— y tuvo que construirse `InventorySlotView` + `InventoryArt` propios. Aquí no
> hace falta pagar ese precio.

**Lo que NO debe ser:** un grafo con zoom y pan al estilo del editor de FSM. El argumento está
ya escrito en `SpellsRuntimeEditor.Tree.cs`: siete nodos en una rejilla 3x3 no necesitan nada
más que un tablero fijo, y una cámara que se puede mover es una cámara que el jugador puede
perder.

**El Grimorio es otro problema y no comparte solución.** 73 hechizos en 9 escuelas no caben en
un tablero fijo; su respuesta es la que el editor de hechizos ya tomó —una escuela cada vez— y
va en su propia auditoría. Lo que sí comparten es el cromo: ventana, cabecera, moneda, tarjeta
de detalle y motas.

---

## 5. Dirección visual

**El tablero es de piedra y el árbol está grabado en él.** El panel del jugador ya estableció el
material (piedra en dos tonos, contorno casi negro de 1 texel, bisel arriba-izquierda, hueco
opaco para lo que contiene un valor) y esa gramática sirve tal cual: un talento es algo tallado,
permanente y que no se devuelve.

### Esqueleto propuesto (en texels, escala 2 a 1600x800)

```text
+--------------------------------------------------------------------------+
| SENDA DE LA MONTANA                     PUNTOS  [ 3 ]         [_] [X]    |  cabecera 20
| "La piedra no esquiva. Simplemente se niega a moverse."                   |  flavour  12
+----------------------------------------------------+---------------------+
|                                                    |  PIEL DE PIEDRA     |
|    ( ) ------------ ( ) ------------ ( )           |  Rango 2 / 5        |
|   PIEL DE         PIEL DE         MANOS            |  -----------------  |
|   PIEDRA          HIERRO          PESADAS          |  Ahora    +24 Vida  |
|   ooooo           ooooo           oooo             |  Rango 3  +36 Vida  |
|     |               |               |              |  -----------------  |
|     v               v               v              |  "Los anos bajo     |
|    ( ) ----------- ( ) ----------- ( )             |   tierra espesan    |
|   BALUARTE       POSTURA DE      CALOR DE          |   mas que la piel"  |
|                  YUNQUE          FRAGUA            |  -----------------  |
|   ooo             ooo             ooo              |  Coste   1 punto    |
|                     |                              |                     |
|                     v                              |  [   APRENDER   ]   |
|                    (*)                             |                     |
|                 INQUEBRANTABLE                     |                     |
|                     o                              |                     |
+----------------------------------------------------+---------------------+
| 8 de 24 puntos gastados            [ REINICIAR TALENTOS ]                |  pie 14
+--------------------------------------------------------------------------+
       tablero 288 texels                      tarjeta 148 texels
```

Medidas: ventana **452 x 246 texels = 904 x 492 px** a escala 2, centrada. Cabe con holgura
sobre el 960x560 de hoy y deja libres las cuatro esquinas (panel del jugador, minimapa, música,
bandeja), que es lo que R13 pide.

- **Nodo:** 32x32 texels, la misma casilla que la barra de acciones. Marco de piedra; hueco
  opaco dentro; icono horneado con `HudTextureBaker.Icon` al tamaño exacto.
- **Pips de rango:** una fila de `maxRank` cuadraditos de 3x3 texels bajo el nodo, llenos hasta
  el rango comprado. Es la redundancia de forma que R6 exige, y es lo que hace que un nodo de 5
  rangos no se parezca a uno de 1 sin leer nada.
- **Aristas:** líneas de 1 texel en la rejilla, en ángulo recto, nunca diagonales. Apagadas
  cuando el prerrequisito no está al máximo; **encendidas en oro cuando se abre el camino**. El
  oro aparece aquí y en el contador de puntos, y en ningún otro sitio: es importancia.
- **Estados del nodo**, los seis, cada uno con forma además de color:
  - *comprado al máximo* — marco de oro, pips llenos, icono a plena luz;
  - *comprado parcial* — marco de piedra clara, pips a medias;
  - *disponible* — borde interior brillante estático (**no parpadeante**, ver sección 6);
  - *bloqueado por prerrequisito* — icono al 35 %, arista de entrada apagada y, al pasar por
    encima, **el prerrequisito que falta se subraya**;
  - *bloqueado por nivel* — candado pequeño con el número;
  - *bloqueado por puntos* — todo como disponible pero el coste en el pie en rojo apagado.
- **Tipografía:** `HudPixelFont`, cara 5x7 para el nombre del árbol y los números, 3x5 para
  costes y pips. TMP **solo** para la prosa de la tarjeta (descripción y flavour), que es
  exactamente lo que R4 permite.
- **La tarjeta de detalle es donde vive el texto largo**, y es la razón de que nada se trunque
  nunca: la frase que hoy se corta a la mitad tiene ahí una columna entera. Muestra **"ahora"
  contra "rango siguiente"**, que es la comparación que el jugador está haciendo y que hoy no
  existe en ninguna parte (`SkillNode.DescribeRank(rank)` ya la calcula: basta llamarla dos
  veces).
- **La placa es OPACA** (alfa 1). El fondo tras la ventana se oscurece con un velo a
  `(0, 0, 0, 0.55)` a orden de ventana, que además señala la modalidad.

### Coherencia: de dónde sale cada pieza

| Pieza | De dónde | Por qué de ahí |
| --- | --- | --- |
| Marco, bisel, piedra, hueco | `HudTheme` + `HudArt` | R2/R3. Es lo que hace que la ventana sea del mismo juego |
| Casilla del nodo | `HudAbilitySlot` (tras el movimiento a `Valkur.UI`) | R5. Cinco estados ya resueltos y ya probados |
| Icono horneado | `HudTextureBaker.Icon` | R10. Un icono de 1024 px minificado por el sampler titila |
| Cabecera, arrastre, cerrar, minimizar, geometría | `WindowChrome` (hoy repartido entre `QuestLogHUD.Window` y `MusicPlayerHUD`) | 5.3. La hoja de personaje es la cuarta ventana que lo necesita: es el momento de extraerlo |
| Tarjeta de detalle | `HudTooltip` generalizada | 5.3. Título, filas de stats en bitmap, prosa en TMP |
| Moneda de puntos | `HudMedallion` | Ya dibuja el nivel en el panel del jugador; un punto de talento es la misma clase de recurso |
| Motas | `HudMoteLayer` | R8. Un `Graphic`, pool fijo, aditivo, texels enteros |
| Banda y orden | `HudLayout` | R9/R13. Y arregla de paso el factor 2.000 |
| Rojo/ámbar/verde de estado | `HudTheme` | R6. Y nunca el azul del maná para un botón |

---

## 6. Partículas: dónde sí y dónde no

La regla es R8 y no admite matices: **un `HudMoteLayer` por ventana, eventos nunca ambiente,
vida ≤ 0.6 s, aditivo, en texels enteros, jamás un `ParticleSystem` en un canvas overlay.**

### Dónde NO, y esto importa más que dónde sí

- **Nada que brille porque un nodo esté disponible.** Es la tentación evidente en un árbol de
  talentos y es exactamente lo que R8 prohíbe: con tres nodos disponibles de siete, casi la
  mitad del panel estaría animada de forma permanente, y a partir de ahí el brillo deja de
  avisar cuando hay motivo. Disponible es un **borde estático brillante**, no una animación.
- **Nada al pasar el ratón.** El hover es una respuesta, no un acontecimiento.
- **Nada al rechazar.** Una compra rechazada no se celebra: el coste tiembla 1 texel y se
  subraya el prerrequisito que falta. Las partículas dicen "ha pasado algo bueno".
- **Nada de fondo.** Ni polvo, ni chispas, ni un árbol que respira. Es una ventana que se abre
  para decidir, no un salvapantallas.
- **Nada en los tabs.** La tira de pestañas es cromo.

### Dónde SÍ (los cinco momentos, y solo esos cinco)

| Evento | Qué hace | Por qué es un evento |
| --- | --- | --- |
| **Aprender un rango** | 8-12 motas salen del nodo hacia fuera, en el color del stat que sube (verde para Vida, azul para Maná, oro para el resto), 0.45 s, con gravedad hacia arriba. El pip que se llena hace *snap* de 1 texel. El contador de puntos baja con un golpe lateral | Es la única acción del panel y hoy no produce un píxel |
| **Abrirse un camino** (un prerrequisito llega al máximo) | La arista se enciende en oro recorriéndola de origen a destino en 0.25 s, y el nodo que se desbloquea recibe un anillo de 6 motas | Es la consecuencia que el jugador acaba de comprar y que no está mirando: su nodo está arriba y lo que cambia está abajo |
| **Comprar el capstone** | Lo anterior x2 más un destello de 0.3 s en el marco de la ventana. Es el único evento que toca el cromo | Un árbol se acaba una vez por partida |
| **Llegar un punto** (subir de nivel, recompensa de misión) | 3 motas caen sobre la moneda de puntos y esta pulsa una vez, 0.5 s. **Y si el panel está cerrado, la insignia de la bandeja se enciende** (`HUDIconBar.SetBadge`, como el inventario) | Es la noticia que el juego hoy no da nunca |
| **Reiniciar talentos** | Las motas van al revés: salen de cada nodo comprado y drenan hacia la moneda, escalonadas 0.03 s por nodo, 0.9 s en total | Es el único evento reversible y debe leerse como tal |

Presupuesto: capacidad 48 motas, un `Graphic`, **≤ 0.05 ms por frame**, y solo mientras la
ventana está abierta. La ventana es modal, así que ni siquiera compite con el combate.

**El ancla física de esta sección:** el panel del jugador ya mide y respeta ese presupuesto, y
sus motas de subida de nivel son el precedente exacto —mismo acontecimiento, otro sitio de la
pantalla. Lo que aquí se añade es que el punto **aterrice** en algún sitio.

---

## 7. Coherencia con el resto del HUD: qué está roto y qué se hereda

| Contrato | Estado hoy en SKILLS | Qué hacer |
| --- | --- | --- |
| R1 espacio de píxel | **Roto.** `ref 800x600 match 0`, factor 2.000 | `HudLayout.Reference*` + `HudRect` |
| R2 un tema | **Roto.** 3 + 8 `new Color(` literales, ningún asset de estilo | `SkillsHudStyle` en `Data/UI/` leyendo `HudTheme` |
| R3 gramática de marco | **Roto.** `Image` sin sprite | `HudArt` |
| R4 dos tamaños, una fuente | **Roto, y peor que roto:** tercera familia (`LegacyRuntime.ttf`) | `HudPixelFont`; TMP solo en la prosa de la tarjeta |
| R5 una sola casilla | **Roto.** Fila de texto | `HudAbilitySlot` tras mover a `Valkur.UI` |
| R6 un color, un significado | **Roto.** Azul de maná en el botón de comprar | Oro = importancia (puntos, aristas abiertas, capstone) |
| R7 movimiento | **Roto.** `SetActive`, sin fundido | Fundido de 0.12 s, lerps del panel |
| R8 partículas por evento | Sin implementar (correcto por omisión, no por decisión) | Sección 6 |
| R9 el espacio se declara | **Roto.** `sortingOrder = 60` a mano, bajo el HUD | Banda de ventana en `HudLayout` |
| R10 iconos horneados | **Roto.** Sin iconos en absoluto | 35 iconos + `HudTextureBaker.Icon` |
| R11 un dato, una lectura | Cumple. Los talentos solo se dibujan aquí | — |
| R12 el HUD también muere | Sin implementar | Seguir `spiritStone` como el resto |
| R13 no abrir sobre un instrumento | **Roto al revés:** abre DEBAJO | Banda + orden de ventana |
| R15 español y teclas reales | **Roto.** Todo en inglés | Traducir UI **y** los 35 `displayName` + 5 nombres de árbol + 5 `flavour` |
| R16 sin `AssetDatabase` en runtime | Cumple por no cargar nada | Referenciar los iconos desde `SkillsHudStyle` |

**Lo que hereda de cada hermana, y por qué de esa:**

- **Del panel del jugador**, el material y la rejilla. Es la superficie más medida del proyecto
  (8.9) y la que fija qué es "piedra" en este juego.
- **Del inventario**, la tarjeta de detalle y la idea de que las filas de números salen de **el
  mismo código que los aplica** (`EquipmentStatSource.AppendItem` + `StatModifier.Describe`
  allí; `SkillNode.DescribeRank` aquí, que ya existe y ya se usa). Y su lección de espacio
  lineal para la opacidad de la placa.
- **De la barra de acciones**, la casilla y sus cinco estados, más la regla de que un botón del
  HUD pasa por el mismo sitio que la tecla equivalente.
- **Del reproductor de música**, el cromo de ventana: arrastre, colapso a la barra de título,
  geometría en `PlayerPrefs` al final del gesto.
- **Del registro de misiones**, la advertencia: es la superficie que ya pagó el factor 2.000 y
  la que descubrió que `TMP_Text.GetPreferredValues` revienta en EditMode.

---

## 8. Roadmap por fases

### Fase 0 — Verdad antes que belleza (1.8 → 3.8)

Sin tocar un píxel del diseño. Todo lo de aquí es defecto, no gusto.

1. `LearnedSkills.AddPoints` dispara también `OnLoadoutChanged`, **o** el panel se suscribe
   además a `OnPointsChanged`. Lo primero es más honesto: recibir puntos SÍ cambia lo que se
   puede aprender.
2. `playerLevel` se relee de `Experience.Level` en cada `Refresh()` en vez de cachearse en el
   bind.
3. `CanLearn` prueba **nivel → prerrequisito → puntos**, en ese orden. El rechazo por puntos es
   el último porque es el único que la cabecera ya está contando.
4. `ScrollRect` en la lista, o el desbordamiento deja de ser silencioso.
5. La etiqueta deja de truncar: `horizontalOverflow = Overflow` no vale —hay que darle el
   ancho. Ancho completo de la fila mientras el botón no exista.
6. No hace falta ningún asset nuevo: esto son unas 40 líneas.

**Cómo se comprueba:** un test que sube de nivel con el panel abierto y lee la cabecera; otro
que pone al jugador con 10 puntos y comprueba que Bulwark dice el prerrequisito y no el coste;
y `characterCountVisible == text.Length` en las siete filas.

### Fase 1 — El árbol (3.8 → 6.0)

1. `git mv` de `SkillTreeHUD` y `SpellTreeHUD` a `UI/HUD/Trees/`, namespace a `Valkur.UI.HUD`.
   Un `using` en `CharacterSheetController` y otro en los tests.
2. Las cinco canvas de la hoja al contrato de `HudLayout` (referencia, match, banda de orden).
   Arregla el factor 2.000 y el dibujarse bajo el HUD de una vez.
3. Layout de tablero desde `row`/`column`, aristas de 1 texel, nodos como `HudAbilitySlot`,
   pips de rango. Placa opaca y velo detrás.
4. Tarjeta de detalle con "ahora / rango siguiente" y la prosa de `description`; `flavour` del
   árbol bajo la cabecera.
5. Botón **REINICIAR TALENTOS** con confirmación de dos clics, como el abandono de misiones.

### Fase 2 — Un solo idioma, un solo lenguaje (6.0 → 7.5)

1. `SkillsHudStyle` en `Data/UI/` + `Resources/UI/`, leyendo `HudTheme`. Cero `new Color(`.
2. `HudPixelFont` en todo salvo la prosa de la tarjeta.
3. Traducción de los 35 `displayName`, los 5 nombres de árbol, los 5 `flavour` y las cadenas
   del panel. Los `skillId` **no se tocan**: son claves de guardado.
4. **35 iconos.** Es el único punto de este roadmap que necesita arte. Mientras no exista, un
   sigilo generado del stat principal del nodo, nunca una casilla en blanco (R10).

### Fase 3 — Feel y partículas (7.5 → 8.6)

1. `WindowChrome` extraído y compartido con misiones y música; geometría persistida.
2. Fundidos de 0.12 s, lerps, el *snap* del pip, el temblor del coste al rechazar.
3. `HudMoteLayer` con los cinco eventos de la sección 6.
4. Insignia en la bandeja y en el verbo "Talentos" cuando hay puntos sin gastar.
5. Modalidad: velo, `InputBlocker`, y decidir si pausa. Recomendación: **no** pausar el mundo
   —Valkur no pausa para el inventario— pero sí bloquear la entrada de combate, que es lo que ya
   hace el chat.

### Fase 4 — Lo que queda abierto

- El Grimorio (73 hechizos, 9 escuelas) necesita su propia auditoría: comparte cromo y no
  comparte layout.
- `CharacterStatsHUD` y `StatisticsHUD` arrastran exactamente los mismos cuatro defectos de
  canvas y fuente; la fase 1 los arregla si se hace por la hoja entera y no por una pestaña.
- Sonido: no hay `skill_*` en `AudioCatalog.asset`, así que cualquier llamada debe ir con
  `HasSfx` delante o suelta un warning por id (la regla del cono de fuego).
- Renombrar **ESC → Skills** a **ESC → Oficios**, que es lo que edita.

---

## 9. Qué NO hacer

- **No añadir zoom ni pan.** Siete nodos en 3x3 caben. Una cámara que se mueve es una cámara
  que se pierde.
- **No hacer que los nodos disponibles parpadeen.** Rompe R8 y mata el aviso de los eventos que
  sí importan.
- **No meter un `ParticleSystem` en el canvas.** No ordena con los `Image` que lo rodean.
- **No arreglar el solapamiento subiendo el `sortingOrder`.** La lección del registro de
  misiones: subir el orden solo cambia cuál de los dos es ilegible. La ventana va a su banda.
- **No renombrar ningún `skillId`.** El tooltip del propio campo lo dice: cada guardado que
  aprendió ese nodo se rompe.
- **No escribir la línea de efecto a mano en `description`.** `DescribeRank` la genera de los
  modificadores justo para que un reajuste no pueda dejar el panel mintiendo.
- **No unificar la moneda con el Grimorio** para "simplificar la UI". Dos monedas es la decisión
  de fondo de `SkillTree` y está argumentada en su propio doc-comment.
- **No copiar `TileEditorTheme` ni `EditorUIHelpers`.** Es la regla que la sección 5.1 del
  lenguaje visual resume: nada que el jugador vea en un build lleva cromo de editor.
- **No empezar por los iconos.** Es lo único que necesita arte y lo último que arregla un panel
  que miente.

---

## 10. Método y reproducibilidad

Medidas tomadas el 2026-09-12, Unity 2022.3.62f1, Play Mode, 1600x800, personaje enano recién
creado (nivel 1, 0 puntos), lobby.

```csharp
// factor de escala de las 36 canvas
foreach (var c in FindObjectsOfType<Canvas>(true)) { /* c.scaleFactor, cs.referenceResolution */ }

// truncado por fila
var gen = label.cachedTextGenerator;
var line = gen.characterCountVisible + " / " + label.text.Length;

// sangrado de la placa: dos capturas con Time.timeScale = 0, abierta y cerrada
ScreenCapture.CaptureScreenshot(path);   // overlay canvas: NUNCA el screenshot del MCP
```

Trampas encontradas al medir, por si vuelven:

- **Un diff de dos capturas sin congelar el tiempo cambia la pantalla entera** (PNJ, clima,
  ciclo de día). La primera pasada dio "cambiado x[0..1599] y[0..799]". Hay que poner
  `Time.timeScale = 0` **antes** de la primera captura, no entre las dos.
- **`ScreenCapture.CaptureScreenshot` es obligatorio**: la herramienta de captura del MCP
  renderiza por la cámara y omite en silencio todas las canvas overlay, o sea el panel entero.
- El `scaleFactor` de una canvas **inactiva** se lee 1.000 aunque esté mal configurada. Las tres
  pestañas cerradas mienten; hay que mirarles `referenceResolution` y `matchWidthOrHeight`.
- La sesión de Play reescribió `particles_instances.json` (196 registros antes y después, solo
  reordenados). Revertido. Conviene mirarlo siempre después de entrar en Play.

---

## 11. Resultado de la reconstrucción (mismo día)

Construido el 2026-09-12, en paralelo con la reconstrucción del GRIMORIO (sesión hermana) y la
de los menús de entrada (tercera sesión). Las tres comparten `HudLayout`, `HudTheme` y el kit de
`Valkur.UIKit`, y se coordinaron por mensaje antes de tocar ningún fichero compartido.

### Qué se construyó

| Fichero | Qué es |
| --- | --- |
| `Gameplay/Player/SkillLock.cs` | Un impedimento como DATO: tipo, número pedido, número que hay, nodo que falta y rangos que le quedan. Gemelo de `SpellLock` |
| `Gameplay/Player/LearnedSkills.cs` | `CollectLockReasons` (todos los impedimentos, en orden nivel → prerrequisito → puntos), `CanLearn` delega; `AddPoints` dispara también `OnLoadoutChanged` |
| `Gameplay/HUD/SkillText.cs` | Las cadenas del tablero, por `GameLanguage.Pick`, con plurales resueltos |
| `Gameplay/UIKit/Hud/SkillTreeLayout.cs` | El tablero como función PURA: `row`/`column` → posiciones y codos ortogonales. Sin GameObject, sin uGUI |
| `Gameplay/UIKit/Hud/HudPixelFont.cs` | Siete glifos nuevos en la cara pequeña: `Á É Í Ó Ú Ü Ñ ¿ ¡`, de siete filas, que el renderizador ya sabía dibujar por encima de la línea de mayúsculas |
| `Data/UI/SkillsHudStyle.cs` | Las ~25 decisiones propias del tablero, en texels y segundos |
| `UI/HUD/Trees/SkillNodeView.cs` | Un nodo: casilla, icono horneado o sigilo, pips de rango, candado, coste y nombre. Seis estados |
| `UI/HUD/Trees/SkillTreeHUD.*.cs` | El panel, en cinco parciales: ciclo de vida, construcción, tablero, tarjeta y motas |
| `UI/HUD/PlayerPanel/HudSlotVerb.cs` + `HudAbilitySlot.cs` | `Badge`: la cuenta que un verbo anuncia con su panel CERRADO |
| `UI/HUD/SpellBar/SpellBarHUD.Verbs.cs` | El verbo Talentos lleva esa insignia con los puntos sin gastar |

Tests: `SkillLockReasonTests` (12), `SkillTreeLayoutTests` (11), `SkillTreeHUDTests` (12
reescritos). La fixture vieja medía `ComputeListText`, un seam de texto que ya no existe.

### Defectos de la sección 3, estado

| # | Defecto | Estado |
| --- | --- | --- |
| 1 | El panel no se entera de que has subido de nivel | **Cerrado.** `AddPoints` dispara `OnLoadoutChanged`; `SkillLockReasonTests.AddPoints_RaisesTheLoadoutEvent` lo fija, y `SkillTreeHUDTests.APointArriving_RepaintsTheBoard` lo comprueba de punta a punta |
| 2 | El 52 % de cada frase se tira | **Cerrado por construcción.** El texto largo ya no está en el tablero: vive en la tarjeta, que tiene una columna entera y el alto que sobra entre los números y el botón |
| 3 | La razón de bloqueo es la equivocada | **Cerrado.** Orden nivel → prerrequisito → puntos, y se devuelven TODOS. La forma del nodo sale de la misma lista que imprime la tarjeta, así que el candado y la frase no pueden discrepar |
| 4 | Siete nodos es el techo | **Cerrado.** El tablero se dimensiona desde el árbol (`ComputeGeometry`), y `TheBoard_GrowsWithTheTree` lo fija |
| 5 | Factor de escala 2.000 | **Cerrado** (lo hizo la sesión del grimorio en las cinco canvas de la hoja; aquí se conserva literal y `TheCanvas_UsesTheSharedHudContract` lo defiende) |
| 6 | La placa es un filtro | **Cerrado.** Piedra opaca de `HudArt.BakePanel` y un velo detrás |
| 7 | El grafo autorado se tira | **Cerrado.** `SkillTreeLayout`, sin un byte de datos nuevos |
| 8 | Cero iconos | **Mitigado.** Sigilo generado por estadística dominante (corazón / gota / cruz) con el color de R6. El arte real sigue pendiente |
| 9 | Cero partículas y cero feedback | **Cerrado.** Cinco eventos, ninguno en reposo, ninguno en un rechazo |
| 10 | Se dibuja debajo del HUD | **Cerrado.** `HudLayout.CharacterSheetSortingOrder` |

### Decisiones que cambiaron durante la construcción

- **El idioma NO se resuelve con una tabla por `skillId`**, como proponía la sección 8. Mientras
  se escribía esto, otra sesión creó `Valkur.Core.GameLanguage` y la del grimorio tradujo sus
  nueve assets de escuela. Dos pestañas del mismo panel comportándose distinto ante el
  interruptor EN es peor que la deuda conocida, así que se siguió el mismo criterio: el CROMO
  pasa por `GameLanguage.Pick` y el contenido AUTORADO se traduce en el asset — que es la
  frontera que el propio doc de `GameLanguage` se pone.
- **`SpellTreeHUD` no se movió.** La auditoría proponía llevar las dos pestañas a `Valkur.UI`;
  la sesión del grimorio lo estaba reescribiendo entero, así que se movió solo `SkillTreeHUD`.
  El movimiento del otro queda para cuando su reconstrucción cierre.
- **El sigilo tiene tres formas y no seis.** El atlas compartido no tiene nada que signifique
  "defensa" ni "daño cuerpo a cuerpo" honestamente, y el candado ya significa "bloqueado" en
  este mismo tablero. Se separan por COLOR, de la tabla de R6, que es un eje honesto en vez de
  uno inventado.
- **Dos clics para comprar un rango**, no uno. Un talento es permanente y el primer clic
  selecciona: es la misma razón por la que abandonar una misión pide confirmación.

### Fallos propios encontrados al revisar, antes de que llegaran a pantalla

- El pie del nodo daba 18 texels a 17 de contenido y el coste se imprimía sobre el nombre.
  `CellFooterHeight` es 26 y `RowPitch` 70.
- La tarjeta se construía contra el alto por defecto y nunca se recolocaba cuando la ventana se
  redimensionaba alrededor del árbol real: el título quedaba fuera por arriba. `LayoutCard()`.
- El *snap* del pip comparaba un float consigo mismo módulo uno y no movía nada; y dos compras
  seguidas habrían horneado el levantamiento de un texel en la posición de reposo.
- El reembolso de un respec subía el saldo, que es el evento "ha llegado un punto", así que el
  reinicio emitía el drenaje Y la llegada. Ahora se re-referencia el saldo en vez de anunciarlo.

### Verificación

Assets, medido fuera de Play Mode:

```text
SkillsHudStyle.asset   creado; Resources.Load lo devuelve; Active == ese asset
traducción             written=40 | memOK=40 memBAD=0 | diskOK=40 diskBAD=0
                       replacementChars=0  accentedChars=44
```

La comparación contra el fichero va sobre el texto DECODIFICADO: Unity serializa `á` como
`á`, así que comparar contra el crudo da 40 falsos negativos. Y los caracteres de reemplazo
se cuentan aparte porque un mojibake lee como "texto no vacío" exactamente igual que una
traducción correcta.

Tests: **44 de 44 en verde**, 2.47 s.

```text
SkillLockReasonTests        12   orden de los gates, todos los impedimentos, el evento
SkillTreeLayoutTests        11   fila 0 arriba, sin solapes, el tablero crece con el árbol
SkillTreeHUDTests           14   canvas, velo, seis estados, contra-escala, desbordes, respec
ShippedSkillTreeDataTests    7   los 35 assets del disco contra lo que el tablero exige
```

### Lo que la captura encontró y ninguna sonda podía ver

Con siete vistas, los estados correctos, la banda de orden correcta y la consola limpia, la
ventana seguía estando mal en cuatro sitios. Los cuatro son de la misma familia: el estado del
objeto era correcto y el PRODUCTO no.

1. **La contra-escala se perdía.** `HudRect.Place` termina con `localScale = Vector3.one` —
   correcto para cualquier hijo en espacio de texel y fatal para el único rect cuyo trabajo ES
   la escala. Medido: panel a 908x544 (454 texels x 2) con el contenido a escala 1, o sea el
   tablero dibujado a la mitad en un cuadrante de su propia piedra.
2. **El título no dibujaba nada.** La cara `Large` de `HudPixelFont` tiene dígitos y
   `/ + - . :` y NI UNA LETRA, y `HudPixelText` salta en silencio lo que la cara no sabe
   deletrear.
3. **El contador de puntos, invisible**: texto casi negro en el centro de un medallón que es un
   ANILLO, o sea sobre la propia piedra oscura.
4. **`SIGUIENTE+36`**: nueve glifos en una columna de 34 texels, comiéndose el valor.

Dos tests nuevos los cazan ya sin Play Mode, y los dos siguen la misma regla —- encontrar la
magnitud que NO depende de que uGUI maquete:

- `ThePixelRoot_CarriesTheCounterScale` afirma `panel.sizeDelta == pixels.sizeDelta *
  pixels.localScale`, cierto a cualquier resolución.
- `NoPixelLabel_OverflowsItsRect` compara `HudPixelText.InkWidth` contra el ancho de su rect;
  `InkWidth` se mide desde la FUENTE, no desde un layout.

### Dos trampas de verificación, medidas

- **Un DLL de tests obsoleto devuelve un rojo perfectamente plausible.** Con un error de
  compilación ajeno bloqueando la pasada, el runner midió código anterior al arreglo:
  `Tests.EditMode.dll 03:52:52` contra `SkillTreeHUDTests.cs 03:54:11`. Devolvió
  `completed=44` y un fallo con su aserción bien formada. El gate son DOS preguntas —
  DLL más nuevo que su propio árbol de fuentes **y** consola sin errores— y la comparación
  tiene que ser POR ENSAMBLADO, o señala al fichero equivocado.
- **Un filtro que no casa nada devuelve verde.** `test_names=["SkillTreeHUD", ...]` dio
  `total: 0, passed: 0, resultState: Passed`. Hace falta el namespace completo.
- **Y el payload trae DOS campos llamados `total`, que no significan lo mismo.**
  `data.progress.total` es el tamaño de la suite ENTERA y vale lo mismo se filtre o no;
  `data.result.summary.total` es lo que el filtro corrió. Medido en los tres runs de esta
  sesión:

```text
filtro sin casar   progress.total 8735   summary.total  0   summary.passed  0   Passed
DLL obsoleto       progress.total 8735   summary.total 44   summary.failed  1
run bueno          progress.total 8741   summary.total 44   summary.passed 44
```

  Así que la guarda no es `total > 0` a secas: es **`summary.total` comparado con lo que el
  filtro debería haber cogido**. Leer `progress.total` da 8 741 con o sin filtro y parece
  tranquilizador justo cuando no debería.

### Un test que suspendía una implementación correcta

`Open_ClaimsEscape_AndCloseReleasesIt` afirmaba `IsClaimed == false` justo después de
`Close()`. `EscapeOwnership.Release` mantiene el claim el RESTO DEL FRAME a propósito, porque
el orden de `Update` entre un overlay y el lanzador no está definido; y en EditMode
`Time.frameCount` no avanza. La API ya ofrecía `IsClaimedOn(frameCount)` con ese motivo escrito
al lado. Cualquier contrato expresado "a partir del frame siguiente" es inafirmable directamente
en EditMode.

### Lo que queda abierto

1. El arte de los 35 iconos. Mientras no exista, el sigilo generado (corazón / gota / cruz) con
   el color de R6 — tres formas y no seis, porque el atlas compartido no tiene nada que
   signifique "defensa" honestamente y el candado ya significa "bloqueado" en este tablero.
2. Renombrar **ESC → Skills** a **ESC → Oficios**, que es lo que edita ese menú. Tiene coste:
   `EditorName` es la clave de los documentos de workspace y de `OwnerEditor`, así que un
   renombrado huérfana los layouts guardados.
3. Mover `SpellTreeHUD` a `UI/HUD/Trees/` cuando su reconstrucción cierre.
4. Los otros diecinueve canvas que siguen poniendo referencia, *match* y escala a mano en vez
   de pasar por `HudLayout.ApplyScaler`.
