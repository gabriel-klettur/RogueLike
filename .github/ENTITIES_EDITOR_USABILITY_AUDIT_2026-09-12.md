# Auditoría de USABILIDAD y FUNCIONALIDAD del Entities Editor

**Auditada:** 2026-09-12 · **arreglada:** 2026-09-13

**Funcionalidad 8.2 · Usabilidad 4.3 · Global 6.3**  ->  **Funcionalidad 8.9 · Usabilidad 8.7 · Global 8.5**

(La sección 7, al final, es lo que se hizo y cómo quedó — medido.)

Todo lo que sigue está **medido en una partida en marcha a 1600x800**, con el editor abierto y
`dark_dwarf` seleccionado: geometría leída de los `RectTransform`, etiquetas leídas de los
`TextMeshProUGUI`, cobertura calculada sobre una rejilla de 8 px, y el catálogo de 28 monstruos
leído del asset. Nada deducido del código.

> **Esto no es la auditoría de esta mañana.** Aquella
> (`ENTITIES_EDITOR_AUDIT_2026-09-12.md`, 5.7 -> 8.8) preguntaba *«¿existe el campo, y hace lo
> que dice?»*. Ésta pregunta *«¿puede un autor terminar la tarea?»* — que es una pregunta
> distinta, y la respuesta es peor. **El editor sabe hacer casi todo y se deja leer y manejar
> mal.**

---

## 0. El resumen en una frase

De las 34 ranuras del Picker, **cuatro pares se dibujan ahora mismo con exactamente la misma
etiqueta**; las **catorce** listas desplegables del editor pintan su flecha como un **cuadro
tofu** y cada una empuja un aviso a una consola que este proyecto exige limpia; **ninguna** de
las cuatro herramientas globales tiene tecla, en el único de los catorce editores que no lee los
verbos compartidos; y la primera frase que lee un autor al abrirlo nombra **F5**, una tecla
retirada el 2026-09-05.

---

## 1. Puntuación por eje de USO

| Eje | Nota | La medida que la fija |
|---|---:|---|
| **Legibilidad** | **3.0** | 4 pares de etiquetas idénticas en pantalla; 14 flechas tofu; cero tooltips |
| **Teclado / economía de gestos** | **2.0** | 0 herramientas en `InputActionCatalog` (Tile 9, Buildings 10); 14º de 14 editores sin verbos compartidos |
| **Precisión de colocación** | **3.0** | Sin snap, sin flechas, sin campo numérico, sin copiar/pegar, sin multiselección |
| **Descubribilidad / orientación** | **4.0** | La línea de estado enseña F5; el tutorial enseña Ctrl+Z/Ctrl+Y, que nadie lee |
| **Seguridad y recuperación** | **6.0** | Colocar y borrar en el mapa no son deshacibles; borrar no confirma. Arrastrar sí deshace |
| **Escala** | **6.0** | Picker reconstruye 34 ranuras por tecla; formulario de 1446 px sin secciones plegables |
| **Consistencia con los otros 16** | **7.0** | Chrome, workspace, zoom y tutorial bien; faltan verbos compartidos y el modal de borrado |
| **Economía de pantalla** | **7.5** | 40.6 % por defecto, 68.2 % con los siete; un solo solape (144x160) |
| **Realimentación** | **8.5** | 81 `SetStatus`; el rename cuenta referencias; el respaldo de animación dicho con palabras |
| **Completitud de autoría** | **8.5** | 60 filas en 8 secciones; `aiTuning` y `coinReward` completos; auto-cast validado |

**Usabilidad** (los seis primeros ejes): **4.3**. **Funcionalidad** (realimentación,
completitud, consistencia, escala): **8.2**.

---

## 2. Los cuatro defectos que un autor encuentra hoy

### 2.1 Cuatro pares del Picker son indistinguibles — leído de la pantalla

Volcado literal de las 34 etiquetas, tal cual se están pintando:

```text
'Barbol B…'  x2   -> Barbol Baby     / Barbol Boss
'Barbol G…'  x2   -> Barbol Gigante  / Barbol Gris
'Barbol M…'  x2   -> Barbol Morado   / Barbol Musgo
'Barbol C…'  x2   -> Barbol Cyan     / Barbol Coloso
'Barbol…  x1'     -> ¿cuál de los once?
```

**20 de los 28 nombres pasan de 9 caracteres**, y `TruncateName(name, 9)` corta a **8 + puntos
suspensivos**. Cuando la ranura además lleva su contador de colocadas — la marca que se añadió
esta mañana — el corte baja a `TruncateName(name, 7)`, es decir **6 caracteres**: los once
barbols del catálogo colapsan en `'Barbol…'`.

**Y hay sitio de sobra.** Medido sobre la propia etiqueta:

```text
ancho del rect de la etiqueta : 66 px
'Barbol Gigante' medido       : 60 px   <- CABE
overflowMode = Overflow, enableWordWrapping = True
```

El recorte es un número de caracteres fijo escrito a mano que no le pregunta al widget cuánto
cabe. Consecuencias: la rejilla responsive que se añadió esta mañana **no revela un solo
carácter más** por mucho que se ensanche el panel, y la marca de «esto está en el mapa» es
justo la que impide saber **cuál**. No hay tooltip en ningún sitio del editor, así que no queda
forma de leer el nombre entero.

### 2.2 Las catorce listas desplegables pintan un cuadro tofu

Catorce avisos en la consola ahora mismo, y los catorce son de este editor:

```text
The character with Unicode value ▾ was not found in the [LiberationSans SDF] font
asset or any potential fallbacks. It was replaced by Unicode character □
```

Reparto: **Animation 6** (State, Variant, Loadout, Layout, Ámbito, Hechizo), **Timeline 4**
(Wind-up, Release at, Launch, Recover at), **Properties / Auto-Cast 4**. O sea: **todos**. El
carácter `▾` no está en la fuente, así que cada desplegable del editor anuncia que es un
desplegable con un `□`. Es un defecto doble — se ve mal, y ensucia la consola que la regla
cardinal del proyecto exige vacía.

### 2.3 El editor no tiene teclado, y su propio tutorial dice que sí

`InputActionCatalog` declara herramientas por editor. Recuento exacto:

```text
Tile Editor       9   (pincel, borrador, relleno, cuentagotas, selección, auto-tile, copiar/cortar/pegar)
Buildings Editor 10
Map Editor        4
Boss Editor       1
Entities Editor   0
```

Y de los verbos COMPARTIDOS (`EditorInput.UndoPressed/RedoPressed/Save`), los leen **trece**
editores: Boss, Buildings, Controls, Death, Economy, FSM, Items, Lighting, Map, MultiSelect,
Skills, Spawners, Tile. **Entities no aparece.** No hay despachador genérico — cada editor lo
lee por su cuenta — así que **Ctrl+Z, Ctrl+Y y Ctrl+S no hacen nada aquí**.

Mientras tanto, su superposición de ayuda, titulada `ENTITIES HOTKEYS`, dice literalmente:

```text
[Ctrl+Z]  [Undo]
[Ctrl+Y]  [Redo]
```

Es la misma clase de defecto que las siete superposiciones que enseñaban teclas F retiradas, en
otro disfraz: el guard escrito esta mañana busca teclas **retiradas**, no teclas que el editor
**nunca leyó**. Y duele el doble porque el deshacer se arregló hoy — cubre ya todas las
mutaciones — y el teclado no lo alcanza.

### 2.4 La primera frase al abrir nombra una tecla muerta

Leído en vivo del `StatusText` al activar:

```text
'Entities Editor active. F5 to close.'
```

Más `Debug.Log("[EntitiesEditor] Activated (F5)")` y su gemelo en `Deactivate`. Los toggles de
la fila F se retiraron el 2026-09-05 y se entra por Escape. El guard de esta mañana exige la
coma de la tupla (`("F5",`) para no marcar los `ToString("F2")`, así que una frase de estado se
le escapa por construcción. No es solo aquí: `BossEditorManager` remite a «the Entities Editor
(F5)» y `Items` dice «Items Editor active. F7 to close.».

**Y la pista de modo nace desincronizada.** `Activate()` hace `_mode = EditorMode.Select;` —
asignación directa al campo, no `SetMode(...)` — así que `RefreshModeButtons()` pinta el botón
correcto y el texto de ayuda se queda en el genérico de construcción. Medido al abrir:

```text
HINT: 'Select a mode then click on the map.'   <- estando YA en modo Select
```

El editor está en un modo y su ayuda dice que elijas uno.

---

## 3. Lo que se mide bien

- **Economía de pantalla, 7.5.** Por defecto cinco paneles y **40.6 %** de la pantalla, sin un
  solo solape. Con los siete abiertos, **68.2 %** de cobertura real (unión sobre rejilla de
  8 px) y **un** solape: Picker x Timeline, 144x160 px. Para un editor de siete paneles es un
  reparto bueno, y la geometría persiste.
- **Realimentación, 8.5.** 81 llamadas a `SetStatus`. El rename cuenta lo que apunta a la clave
  antes de tocarla; el panel de Animation dice **con palabras** a qué estado cae el respaldo
  cuando no hay arte; el selector de boca da la fracción **y** las unidades de mundo; los diales
  de esquiva avisan si el set FSM no declara `DodgeState`. Ese nivel de «te digo por qué» es lo
  mejor del editor.
- **Completitud, 8.5.** 8 secciones, **60 filas**, `aiTuning` entero y `coinReward`. El
  auto-cast se valida contra el catálogo de hechizos y rechaza una clave desconocida con su
  motivo.
- **Coste de la tarea frecuente.** Colocar una entidad son **dos gestos** (arrastrar la ranura
  al mapa) o cuatro clics por la ruta de modos. Afinar la esquiva: escribir `dodge` en el filtro
  deja **5 filas de 1 sección**. Eso está bien resuelto.

---

## 4. Lo que falta, funcionalmente

### 4.1 Colocar y borrar no se deshacen; arrastrar sí

Es la inconsistencia más difícil de explicarle a un autor. `PlaceEntityFromDrag` no graba nada;
`DeleteEntityAtPosition` hace `Destroy` y `MarkEntityPlacementsDirty()` — el autosave lo
escribe — y no graba nada **ni pregunta**. El arrastre de una entidad colocada sí graba, porque
era el único gesto cubierto antes de los arreglos de hoy.

Así que el editor deshace **cada edición de una definición** y no deshace **las dos operaciones
de mapa más frecuentes**, que son además las únicas destructivas. Y el editor de Buildings sí
tiene modal de confirmación de borrado.

### 4.2 Una colocación no se puede posicionar, solo arrastrar

No hay snap a rejilla, no hay desplazamiento con flechas, no hay campo numérico de posición, no
hay copiar/pegar y no hay multiselección. Tile tiene copiar/cortar/pegar, Buildings tiene
copiar/pegar, y existe un editor MultiSelect entero con los suyos. Aquí, el único dato propio
de una colocación — **dónde está** — se autora exclusivamente con el ratón, a pulso. Colocar
seis guardias en fila es seis arrastres y ninguna forma de alinearlos.

### 4.3 El formulario no se pliega

Medido con `dark_dwarf` seleccionado:

```text
altura del formulario : 1446 px
altura del viewport   :  503 px   -> se ve el 34.8 %
secciones: 8   filas: 60
```

`MakeFormSection` monta la cabecera como `Image` + texto: **no es un botón, no pliega**. Así
que ajustar un dial de IA obliga a pasar por Identity, Stats, AI y Spawn cada vez. El filtro lo
alivia, pero exige saber **cómo se llama** el campo — y es justo lo que no sabe quien está
explorando.

### 4.4 Deuda que escala

El Picker destruye y recrea sus 34 ranuras en cada pulsación de tecla de la búsqueda. Con 28
entidades no se nota; es la forma que costó 213 ms por carácter en el editor de Controles y
3.5 s en el de Items. Sigue siendo deuda, no defecto.

---

## 5. Qué haría, por rentabilidad

1. **Que la etiqueta del Picker pregunte cuánto cabe** en vez de cortar a 9 y a 7. El rect mide
   66 px y `'Barbol Gigante'` mide 60: hoy se tira información que ya cabía, y es lo que hace
   que ocho ranuras sean cuatro parejas idénticas. Con el panel redimensionable de esta mañana,
   ensanchar pasaría a servir para algo. Y un tooltip con el nombre y la clave completos.
2. **Cambiar `▾` por un glifo que la fuente tenga** (o dibujar la flecha). Arregla catorce
   desplegables y catorce avisos de consola de una vez.
3. **Leer los verbos compartidos** — `EditorInput.UndoPressed/RedoPressed`, y el Save — para que
   el tutorial deje de mentir y el deshacer que se arregló hoy tenga teclado. Es la línea que
   trece editores ya tienen.
4. **Grabar colocar y borrar en la pila**, y confirmar el borrado como hace Buildings. Son las
   dos operaciones destructivas y las dos únicas sin red.
5. **Quitar el F5 de la línea de estado y de los dos `Debug.Log`**, y ampliar el guard para que
   mire cadenas de estado además de filas de tutorial. De paso, Items y Boss.
6. **Llamar a `SetMode(EditorMode.Select)` en `Activate`** en vez de asignar el campo, para que
   la ayuda diga el modo en el que ya está.
7. **Cabeceras de sección plegables** en Properties, recordando el pliegue en el workspace. 1446
   px en un hueco de 503 es tres pantallas de scroll.
8. **Flechas para desplazar la selección** y un campo numérico de posición. Es el mínimo para
   alinear dos cosas.

---

## 6. Nota sobre el método

Dos cosas de esta auditoría solo se ven mirando la pantalla, no leyendo el código:

- **Las etiquetas idénticas.** `TruncateName(name, 9)` se lee perfectamente razonable. Lo que la
  condena es medir el rect (66 px) contra el texto completo (60 px) — dos números que el código
  nunca pone juntos.
- **La pista de modo desincronizada.** `Activate()` pone el modo y refresca los botones; parece
  completo. Solo leer el `TextMeshProUGUI` dice que el texto se quedó donde lo dejó el
  constructor.

Y una advertencia para la próxima sonda: **forzar paneles a visibles para medirlos cambia el
workspace del autor**, porque `EditorWorkspaceService` captura el estado abierto/cerrado al
cerrar. Los dos que se activaron aquí (Animation, Timeline) se devolvieron a inactivos y se
reconcilió con `SyncDropdownStateFromPanels` antes de desactivar.

---

## 7. Lo que se arregló, y cómo quedó — medido

Ronda el 2026-09-13, atacando de la nota más baja hacia arriba. Suite completa
**8911/8911**, consola a cero.

| Eje | Antes | Después | Qué cambió |
|---|---:|---:|---|
| **Teclado** | 2.0 | **9.0** | Lee los verbos compartidos; mapa `Editor.Entities` con 5 herramientas |
| **Legibilidad** | 3.0 | **9.0** | 0 etiquetas duplicadas, 0 cuadros tofu, tooltip en cada ranura |
| **Precisión** | 3.0 | **8.0** | Flechas, Shift fino, snap a rejilla, X/Y numéricos |
| **Descubribilidad** | 4.0 | **8.5** | Sin teclas muertas; la ayuda nace en el modo correcto; tutorial cierto |
| **Seguridad** | 6.0 | **9.0** | Colocar y borrar deshacibles; mover por fin se guarda |
| **Escala** | 6.0 | **8.0** | Secciones plegables: del 34.8 % visible al 78.6 % |
| **Consistencia** | 7.0 | **9.0** | 14º de 14 editores que faltaba, alineado |

**Usabilidad 4.3 -> 8.7. Global 6.3 -> 8.5.**

### Las etiquetas del Picker

`TruncateName` **borrado**, no dejado sin usar: un ayudante que corta a un número fijo de
caracteres *es* el defecto, y dejarlo ahí es dejarle algo cómodo y equivocado al siguiente que
construya una ranura. Ahora `TextOverflowModes.Ellipsis` más auto-tamaño 7-9 pt. Volcado
literal de las 34 etiquetas ya pintadas:

```text
'Barbol Baby' 'Barbol Boss' 'Barbol Gigante' 'Barbol Gris' 'Barbol Morado'
'Barbol Musgo' 'Barbol Cyan' 'Barbol Coloso' 'Barbol Amarillo 4' 'Dragon Rojo'
duplicados = 0     (antes: 4 pares idénticos)
```

El contador de colocadas pasó a **insignia de esquina**: era lo que bajaba el presupuesto de 9
a 7 caracteres, así que la marca de «esto está en el mapa» era justo la que impedía saber
cuál. Medido: 5 colocadas, 5 insignias, y `Barbol Oscuro` y `Dragon Rojo` legibles enteros
junto a la suya.

**Un quinto defecto encontrado al arreglar el cuarto:** las ranuras pintaban su tinte con
`btn.GetComponent<Image>().color`, que es exactamente lo que `UIButton.SetTint` documenta como
imposible — un `Selectable` en ColorTint multiplica el color del CanvasRenderer con el del
Graphic y lo reescribe desde `colors.normalColor` en cada transición. Era el último sitio del
editor que lo hacía. Va por `EditorUIHelpers.SetSlotTint`.

### Los cuadros tofu, y su alcance real

`▾` no está en `LiberationSans SDF`. Y elegir otra flecha no era la solución: preguntada
directamente, la fuente **no tiene ninguna** de `▾ ▼ ▽ ▴ ↓ ∨ ▪ ● ▶ → ─ − ≤ ≥ ≈ ⚙ ♪ ↔`. Así que
el cursor se **dibuja** (`Valkur.UIKit.CaretGraphic`) y el resto pasa a ASCII.

Deliberadamente NO es `TriangleHandleGraphic` con un quinto valor de enum: esa clase comparte
`ResizeGripCorner` con `PanelResizeHandle` precisamente para que el glifo y el arrastre que
anuncia no puedan nombrar esquinas distintas, y meterle un valor que a un redimensionado no le
dice nada es el acoplamiento que esa garantía existe para evitar.

El barrido destapó que **el problema no era de este editor**: **87 sustituciones en 40 ficheros**
de trece editores — los separadores `─` de Items y Spells, sesenta flechas `→` en mensajes de
estado. Guard nuevo: `EditorFontGlyphCoverageTests`, que pregunta a la fuente en vez de llevar
una lista.

### El teclado

Trece editores leen `EditorInput.UndoPressed/RedoPressed/SavePressed`; no hay despachador
genérico. Este era el que faltaba. Y `InputActionCatalog` gana el mapa `Editor.Entities` con
cinco herramientas — flechas, y `G` para el snap — con `OwnerEditor` igual al `EditorName`
literal, que es lo que `InputContextPolicy.IsLive` compara.

Guard nuevo: **toda tecla que el tutorial enseña tiene que ser una que el editor lea**. El
guard de teclas F existente busca teclas **retiradas** y por construcción no puede ver una que
nunca se cableó — que es como `Ctrl+Z` y `Ctrl+Y` llevaban toda la vida del editor en esa
superposición.

### Colocar, borrar y mover

Los dos primeros ahora graban en la pila; una colocación se restaura **por su id**, no por su
objeto, porque tras un borrado el objeto ya no existe y guardar la referencia sería guardar una
colgada — y reaparecer con el mismo `PlacementId` es lo que evita que un override `by_eid` del
FSM lo lea como borrar-y-crear.

Deliberadamente **no** es un diálogo de confirmación: un modal por clic es un clic de más en
los noventa y nueve borrados que sí querías, para proteger el uno que no. Deshacer protege los
cien.

**Y un defecto real destapado al cablear la flecha:** el propio comentario de
`MarkEntityPlacementsDirty` dice que «lo llama toda mutación (colocar, borrar)» — y MOVER no
era una de ellas. Arrastrar un monstruo cambiaba el mundo, empujaba un paso de deshacer y **no
programaba ninguna escritura**: la posición nueva sobrevivía a un Stop solo si alguna edición
posterior ensuciaba el fichero por su cuenta.

### El formulario

Cabeceras plegables. Medido sobre `dark_dwarf`: 60 filas, **1446 px -> 640 px** en un viewport
de 503, o sea del **34.8 % visible al 78.6 %**. El pliegue se recuerda **por nombre**, porque
el editor destruye y reconstruye cada cuerpo al cambiar de selección y unas referencias a
Transform tendrían objetos destruidos. Y mientras hay filtro el pliegue se suspende: una
sección plegada que no contestara al filtro sería teclear el nombre de un campo y que te digan
que no hay coincidencias con la coincidencia a una cabecera de distancia.

### Trampas de esta ronda

- **El ratchet de colores contaba su propia explicación.** Un comentario diciendo «no escribas
  `new Color(...)` aquí» puntuaba como color a pelo: documentar la regla junto al código que
  gobierna hacía fallar el guard, y la única forma de pasar era borrar la frase que impide que
  el siguiente reintroduzca el defecto. Medido: +1 sin haber añadido ningún color. Ahora quita
  comentarios, como ya hacía el guard de `SaveAssets()` por lo mismo.
- **`Object.Destroy` es diferido, y una sonda que hace tres cosas en una llamada las mide en un
  solo fotograma.** Las filas salían x3 y las colocaciones descuadradas. No era un fallo: era la
  sonda. Pero destapó una ventana real — deshacer y rehacer en el mismo fotograma dejaba dos
  objetos con un id — cerrada desactivando antes de destruir, el mismo truco que
  `PersistentEventSystem` usa con su duplicado.
- **`\n` dentro de un heredoc se colapsa en un salto real** y parte un literal de C# en dos.
  Ya estaba escrito en CLAUDE.md; volvió a morder.
- **Un guard con falsos positivos es un guard que alguien apaga.** El primer barrido de teclas F
  marcó `{valor:F2}` y habría condenado «Shift+F8 to toggle», que es CORRECTO — las sondas de
  rendimiento de Tile y Buildings sí son dueñas de F2-F8. Busca la FRASE («F5 to close»), no la
  tecla suelta.
