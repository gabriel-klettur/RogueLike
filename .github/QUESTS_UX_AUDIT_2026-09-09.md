# Misiones — auditoría de UI/UX del recorrido completo

> Auditoría del flujo entero, desde que el jugador se acerca a un personaje hasta que
> cobra la recompensa. Fecha: **2026-09-09**. Todo lo que se afirma aquí está medido
> sobre un frame real en Play Mode a 1600x800, no leído del código.

## Veredicto

El sistema de misiones **funciona y no se puede seguir**. Las diez misiones se aceptan,
avanzan, se guardan y pagan — y el jugador no tiene forma fiable de saber en qué punto
está: el seguidor de la esquina estaba **tapado por el minimapa**, el personaje que te
mandó al bosque **se olvidaba de ti** en cuanto aceptabas, y completar una misión no
producía **ni un solo pixel** de confirmación.

**Nota antes: 2.4 / 10. Tras la primera pasada: 5.4. Tras la segunda: 7.2 / 10.**

> **Segunda pasada (mismo día).** Marcadores de misión en el minimapa, marca sobre la
> cabeza del NPC, misiones bloqueadas visibles con su requisito, tareas listadas antes de
> aceptar, y abandonar desde el panel. Detalle en la sección 5.

## Puntuación por eje

| # | Eje | Antes | 1ª pasada | 2ª pasada | Qué lo mueve |
|---|---|---|---|---|---|
| 1 | Descubrimiento (saber que hay misión) | 3.5 | 3.5 | **7.5** | Marca sobre el NPC + bloqueadas visibles |
| 2 | La hoja de oferta | 4.0 | 7.0 | **8.0** | Tareas listadas antes de aceptar |
| 3 | Feedback al aceptar | 2.0 | **7.0** | 7.0 | Acuse propio en vez de repetir el discurso |
| 4 | Seguimiento durante el juego | 2.0 | **6.0** | 6.5 | Fuera del minimapa, con título y TMP |
| 5 | Progreso visible en el dador | 0.5 | **7.0** | 7.5 | Sección "EN CURSO" con objetivos |
| 6 | Entrega | 5.0 | 6.5 | **7.5** | Un solo marcador, y con el nombre real |
| 7 | Recompensa y cierre | 0.5 | **6.5** | 6.5 | Toast con lo que ha pagado |
| 8 | Orientación (dónde ir) | 0.5 | 0.5 | **8.0** | Marcadores de oferta, objetivo y entrega |
| 9 | Control y recuperación | 1.5 | 1.5 | **7.5** | Abandonar con confirmación, desde el panel |
| 10 | Consistencia visual | 3.0 | **7.0** | 7.5 | TMP y paleta del proyecto |
| 11 | Ocupación de pantalla / solapes | 2.0 | **8.0** | 8.0 | El solape medido, eliminado |
| 12 | Idioma y accesibilidad | 4.0 | 4.0 | **5.0** | Glifo distinto por estado, no solo color |

---

## 1. El solape: medido, no supuesto

El informe era "el minimapa lo tapa". Medido en Play Mode a 1600x800:

| Elemento | Rect | Orden de dibujo |
|---|---|---|
| Panel del seguidor de misiones | `x[1152..1584] y[440..736]` | **40** |
| Minimapa | `x[1384..1576] y[540..776]` | **105** |

**Solape: 192 x 196 px = 29.4 % del seguidor**, y el minimapa gana porque dibuja a 105
contra 40. Peor aún: cae sobre la **esquina superior derecha**, que es exactamente donde
corren los nombres de las misiones y sus contadores `3/6`.

Un barrido completo sobre todo lo que cruza ese rectángulo deja claro que el minimapa era
el único culpable permanente:

| Encima del seguidor | Cobertura | ¿Real? |
|---|---|---|
| `MinimapHUDCanvas/Root` (105) | 29.4 % | **Sí — siempre en pantalla, opaco** |
| `InventoryCanvas/InventoryPanel` (200) | 69.4 % | No: modal, solo con el inventario abierto |
| `ChatCanvas/Backdrop` (200) | 100 % | No: alpha 0, solo captura clics |
| `DayNightVignetteCanvas/Vignette` (95) | 100 % | No: degradado, no oculta texto |
| `HUDCanvas/BossHealthBarHUD` (100) | 100 % | No: contenedor vacío salvo con jefe |

**El panel de diálogo NUNCA estuvo tapado.** Medido: el chat vive en `x[20..720]
y[20..332]` (abajo a la izquierda) y su canvas dibuja a **200**, por encima del minimapa
(105). Lo que estaba tapado era el seguidor de misiones, que es la parte del sistema de
misiones que vive en esa esquina.

### Por qué pasó, y por qué no puede repetirse

Los dos widgets estaban escritos **en idiomas de maquetación distintos**: el minimapa en
píxeles fijos (margen 24, disco 192) y el seguidor en **fracciones de pantalla**
(`0.72–0.99` en x, `0.55–0.92` en y). Dos sistemas que solo pueden coincidir por
casualidad — y no coincidían.

Y no podían ponerse de acuerdo ni en principio: el minimapa está en `Valkur.UI` y el
seguidor en `Valkur.Gameplay`, y **`Valkur.Gameplay → Valkur.UI` es una referencia
prohibida**. Por eso la constante compartida vive ahora en `Valkur.Core.UI.HudLayout`,
que es donde este proyecto ya pone lo que dos ensamblados tienen que compartir
(`SortingConfig` está ahí por lo mismo).

`HudLayout.BelowMinimapTop` es **derivado** del propio bloque del minimapa, así que mover
el minimapa mueve lo que hay debajo en lugar de deslizarlo silenciosamente por detrás.

### El segundo defecto que salió con el primero

El `CanvasScaler` del seguidor estaba en el **800x600 por defecto de Unity con
`matchWidthOrHeight = 0`**, mientras todo el resto del HUD usa 1600x800 con match 0.5.
A 1600 de ancho eso es un factor de escala de **2.0 contra 1.0**: su texto se renderizaba
al doble de tamaño que cualquier otro HUD, y las dos esquinas se separaban en cada
redimensionado. Ahora ambos leen `HudLayout`.

Subir el seguidor por encima del minimapa habría sido el arreglo equivocado: solo cambia
cuál de los dos es ilegible. **Apilarlos** es la única disposición en la que se leen los
dos.

### El tercer solape, que apareció al arreglar el primero

Bajar el seguidor lo metió debajo de otra cosa. Barrido tras el primer arreglo:
**`MusicHUDCanvas/MusicPlayerHUD`, orden 150, cubría el 35.1 % con alpha 0.85** — opaco y
por encima. Medido, ocupa `x[1211..1568] y[208..344]`.

Eso deja una banda libre de solo **172 px** entre el minimapa y el reproductor, y ahí una
constante no sirve: **el reproductor es arrastrable y su geometría se guarda por máquina
en PlayerPrefs**, así que cualquier reserva calculada contra su sitio actual está mal para
el jugador que ya lo movió.

La respuesta no es reservar más, es **ocupar menos**: el panel se dimensiona con
`GetPreferredValues` a partir del texto que realmente tiene, con techo en la banda libre.
Un seguidor con una misión corta ya no pinta un rectángulo oscuro de 378 px sobre el
mundo, y un jugador que haya recolocado el reproductor no arrastra una reserva pensada
para donde estaba antes.

### Resultado, medido con dos misiones activas

| Elemento | Rect | Solape con el seguidor |
|---|---|---|
| Seguidor | `x[1276..1576] y[376..528]` | — |
| Minimapa | `x[1384..1576] y[540..776]` | **0 px** (hueco de 12) |
| Reproductor | `x[1211..1568] y[208..344]` | **0 px** (hueco de 32) |

Bordes derechos alineados en 1576: los tres widgets leen como una columna, no como tres
cosas que casualmente están a la derecha.

---

## 2. El recorrido, paso a paso

### Paso 1 — Descubrir que hay una misión — **3.5**

El jugador se acerca, ve el badge `Conversar`, entra, y el personaje **suelta la frase de
gancho**. Eso es todo lo que hay. Funciona, y tiene dos huecos:

- **No hay indicador sobre el NPC.** El clásico signo de exclamación no existe, así que
  saber quién tiene trabajo exige entrar en conversación con los siete personajes.
- **Lo que aún no puedes coger es invisible.** `OffersFor` filtra por nivel y
  prerrequisitos, así que una misión de nivel 10 sencillamente no aparece — el jugador no
  puede ver hacia dónde crece. Enseñarla en gris con su requisito sería la respuesta.

### Paso 2 — Abrir la hoja de misiones — **4.0 → 7.0**

El botón **Misiones** aparece en la columna izquierda del panel, condicional como
Comerciar. La hoja cubre la conversación, deja cara, título y columna visibles, y lista
primero lo que está listo para entregar.

**Lo que estaba roto y ya no:** la tarjeta tenía una altura fija de **78 px** para tres
bloques de texto — nombre en negrita, el gancho del personaje (los enviados son de dos o
tres frases) y la línea de recompensa —, envueltos en unos 434 px a cuerpo 11. El gancho
de Gatita solo ocupa seis líneas así, de modo que **el párrafo que la hoja existe para
enseñar era justo lo que se recortaba con puntos suspensivos**. Ahora la tarjeta se
dimensiona con `GetPreferredValues` y 78 px es un **suelo**, no la altura.

Sigue faltando: barra de scroll visible, y un desglose de objetivos en la oferta (el
jugador acepta sin ver qué le van a pedir exactamente).

### Paso 3 — Aceptar — **2.0 → 7.0**

Pulsabas **Aceptar** y el personaje **repetía el gancho palabra por palabra**. Ese
párrafo ya estaba en pantalla dos veces: hablado en la transcripción al abrir el panel, e
impreso en la tarjeta que acabas de pulsar. La tercera lectura no confirma nada — se lee
como que el botón ha fallado y lo único que ha hecho es hacer scroll.

Ahora dice un acuse propio (`ChatLanguage.QuestAccepted`) y la misión desaparece de la
lista de ofertas, que es la confirmación que no se puede confundir con un botón inerte.

### Paso 4 — Jugarla — **2.0 → 6.0** (seguidor) y **0.5 → 7.0** (en el dador)

Dos fallos distintos, y el segundo es el peor de toda la auditoría.

**El seguidor** estaba bajo el minimapa (arriba), usaba `Text` heredado con
`LegacyRuntime.ttf` cuando el proyecto entero es TextMeshPro, no tenía título, y era una
caja negra al 55 % sin jerarquía. Ahora: TMP, título `MISIONES`, paleta del proyecto,
apilado bajo el minimapa, y `raycastTarget = false` — porque un blanco de raycast sobre un
cuarto de pantalla se come el zoom de rueda de la cámara a través de
`EventSystem.IsPointerOverGameObject`.

**El dador te olvidaba.** Una misión aceptada sale de `OffersFor` y no llega a
`TurnInsReadyFor` hasta estar terminada. Entre esos dos momentos —es decir, durante toda
la misión— el personaje que te mandó mostraba una hoja vacía con "no tiene nada para ti",
**y el botón Misiones desaparecía por completo**. Se lee como que el encargo nunca
existió. Ahora hay una sección **EN CURSO** con cada objetivo y su contador, y el botón
sigue en la columna mientras haya trabajo suyo abierto.

### Paso 5 — Entregar — **5.0 → 6.5**

El botón **Entregar** no completa la misión: dispara el mismo `OnNpcConversed` que dispara
la conversación, y el objetivo de entrega generado es quien se entera. Una sola ruta de
cierre — correcto, y con un efecto secundario que sí era un defecto: `HandleNpcConversed`
volvía a ejecutarse y **repetía la línea de cierre** que ya había dicho al abrir el panel.

Ahora hay un registro por conversación (`_announcedThisConversation`), que se limpia al
cerrar el chat. Se limpia al CERRAR y no al abrir a propósito: `OnChatOpened` y el primer
`OnNpcConversed` caen en el mismo frame en un orden que nada fija, y limpiar en el lado
equivocado borraría la entrada recién escrita.

### Paso 6 — Cobrar — **0.5 → 6.5**

Completar una misión **no producía ninguna confirmación en ninguna parte**. Las
recompensas caen en cuatro sistemas distintos —experiencia, puntos de talento, puntos
arcanos, monedas, objetos— y la única evidencia era que una línea desaparecía del
seguidor. Un jugador no puede distinguir una misión que ha pagado de una que ha fallado en
silencio.

Ahora `QuestService.AnnounceCompletion` saca un toast con el nombre y lo que ha pagado. Va
por el **toast y no por la conversación** porque una misión también se puede cerrar con un
sondeo estando el jugador lejos de cualquiera — un `Collect` que termina al recoger el
último mineral.

---

## 3. Lo que sigue abierto, por apalancamiento

1. **Felipondor no está en el mundo.** Dos de las diez quests tienen un dador que ningún
   spawner instancia. Es lo más barato con más efecto: dos quests enteras pasan de
   inalcanzables a jugables colocando un spawner.
2. **Sonido.** Aceptar, avanzar un objetivo y completar no suenan, y el catálogo no tiene
   ids para ello — habría que gatear en `HasSfx`, porque un id inexistente es un warning
   por sesión en una consola que este proyecto exige limpia.
3. **El seguidor no tiene tecla.** Aparece y se esconde solo; no se puede consultar a
   voluntad ni hace scroll con muchas misiones. Añadir una tecla significa una acción nueva
   en `ValkurInputActions` **más** su descriptor en `InputActionCatalog` (tabla cerrada: una
   acción sin descriptor es un test rojo) — y ese asset está modificado sin commitear por
   otra tanda, así que tocarlo ahora es pedir un conflicto.
4. **Brújula fuera de pantalla.** Los marcadores solo existen dentro del radio del
   minimapa; un objetivo lejano no tiene indicación de rumbo.
5. **Barras por objetivo en el seguidor.** Hoy es texto `3/6`; una barra se lee de un
   vistazo.
6. **Misiones repetibles y ramas.** `Quest` sigue siendo AND puro.

---

## 5. Segunda pasada: orientación, descubrimiento y control

### El muro que hacía imposible la navegación

La orientación puntuaba 0.5 y no era por dificultad: **no había canal**. El minimapa es
`Valkur.UI`, la capa de misiones es `Valkur.Gameplay`, y **ninguno de los dos puede
referenciar al otro** — `Gameplay → UI` es la arista circular prohibida y UI tampoco
referencia Gameplay. `MinimapMarker` es un MonoBehaviour, así que publicar a través de él
habría sido el mismo muro.

`Valkur.Core.UI.WorldMarkerBoard` es el canal: datos planos (posición, motivo, etiqueta) en
un ensamblado que está por debajo de los dos. Va **con canal desde el primer día** aunque
hoy solo publique uno, porque un tablero que reemplaza todo su contenido borraría los
marcadores de misión en cuanto algo más quisiera publicar — y el fallo sería un marcador que
desaparece sin causa visible.

### Lo que se marca, y lo que deliberadamente no

| Objetivo | ¿Marcador? | Por qué |
|---|---|---|
| `Talk` | sí — posición viva del NPC | Cinco de siete personajes **andan**; una coordenada fija apuntaría a donde estaba |
| `Reach` | sí — centro de la zona | |
| `KillCount` | sí — el monstruo vivo **más cercano** | No hay dirección fija; si no vive ninguno, silencio |
| `Collect` / `Craft` / `CastSpell` / `Survive` / `ReachLevel` / `EarnCoins` | **no** | No tienen sitio. Inventarlo enseña al jugador a desconfiar de todas las flechas |

Un objetivo ya completado tampoco marca: una chincheta que se queda es peor que ninguna,
porque manda al jugador a algo que ya hizo.

### Dos defectos que solo aparecieron midiendo el frame

Con la quest de los recados aceptada y terminada, el barrido en vivo mostró:

1. **Dos marcadores sobre Abigail** — el de entrega (cruz verde grande) y otro de objetivo
   (rombo azul) encima, porque el paso de entrega *generado* también se localizaba. La cruz
   que más importa quedaba emborronada por un punto más pequeño. Ahora el barrido de
   objetivos recorre solo los **autorizados**, con la misma regla de posición que usa
   `QuestService.AuthoredWorkDone`, para que las dos no puedan divergir.
2. **`Vuelve a hablar con vendor_banker_abigail`** — la clave de base de datos, impresa al
   jugador en la única frase que le dice adónde ir. Ahora resuelve el nombre real.

Medido después: `Vuelve a hablar con Abigail`, y **3 marcadores en vez de 4**.

### Un bug que encontró el test antes que el juego

`QuestBadgeState` se compara por valor — el publicador se queda con el estado **más
urgente** de un personaje que es varias cosas a la vez. El primer corte lo numeró
`Offer=1, TurnIn=2, InProgress=3`, así que **`InProgress` ganaba a `TurnIn`**: un personaje
que te debía una recompensa habría mostrado la marca apagada de "estás trabajando en algo",
el único estado con el que el jugador no puede hacer nada. Reordenado a
`InProgress < Offer < TurnIn` y fijado por test.

### Verificación en vivo del flujo entero

Aceptada `q_cartas_caminos` (3 conversaciones + una zona) sobre el mundo real:

```
markers=7
  QuestObjective (212,93) 'Pavel'     QuestObjective (194,93) 'Valeria'
  QuestObjective (161,79) 'Roberto'   QuestObjective (125,75) 'Forest'
  QuestObjective (193,77) 'Abigail'   QuestOffer (166,61) 'Gatita'
  QuestOffer (163,92) 'Smith'
badges: Abigail=InProgress  Smith=Offer  Gatita=Offer
```

Y tras completar los cuatro objetivos: `Abigail=TurnIn`, 3 marcadores, la línea con su
nombre. Las posiciones son las vivas de los NPCs, no coordenadas autorizadas.

### Hallazgo de contenido: dos quests con un dador que no existe

Medido sobre el mundo cargado: **6 personas vivas, 7 dadores en el catálogo**.
`npc_barbol_brother_felipondor` es una persona y una `MonsterDefinition`, y **ningún spawner
lo instancia** — así que `q_cripta_colina` y `q_vigilia_altar` son inalcanzables jugando.

La interfaz se comporta bien ante eso (sin oferta, sin marca, sin marcador: simplemente no
existen), así que es un hueco de **datos**, no de UI. No se ha tocado el mundo: colocar un
spawner es dato compartido y la decisión de dónde vive Felipondor es de diseño. Es el
primer punto de la lista de abajo.

## 4. Método

Todo lo cuantitativo de este documento sale de un frame vivo, no de leer código: los
rects son `GetWorldCorners` sobre los `RectTransform` reales, y los órdenes de dibujo son
`Canvas.sortingOrder` leídos de la escena en ejecución.

Merece la pena registrar **un fallo de medición cometido durante la propia auditoría**,
porque es la trampa que este repositorio ya documenta. El primer barrido de solapes
filtraba los canvas con `GetComponentInParent<Canvas>() != cv` — y ese método **ignora los
objetos inactivos**, así que devolvía null para todo canvas apagado y el filtro descartaba
en silencio a todos los canvas con padre, **incluido el minimapa**. El barrido devolvió
una lista larga, plausible y sin el único elemento que importaba. Una cifra puede ser real,
internamente consistente, y hablar de otra cosa distinta de la que preguntaste.

Y merece la pena registrar que **volver a medir después del arreglo es lo que encontró el
reproductor de música**. Un arreglo de maquetación no está verificado por el hecho de que
el solape original haya desaparecido: mover un panel es meterlo en el sitio de otro, y eso
solo se ve repitiendo el barrido completo sobre la posición nueva.

## 5. Estado de la verificación

- Consola de Unity limpia: 0 errores, 0 avisos accionables.
- **Suite completa EditMode: 8189 / 8189 en verde** (151.9 s), incluidos los 21 tests
  nuevos de `QuestNavigationTests`.
- Los 16 rojos que reportó la primera pasada eran de otra tanda en curso (expansión de
  capas visuales 9→16) y ya están cerrados; no se tocó ninguno.
- Flujo verificado en Play Mode sobre el mundo real, no en fixtures: marcadores, insignias,
  promoción a entrega y texto con nombre propio.
