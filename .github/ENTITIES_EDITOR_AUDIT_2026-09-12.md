# Auditoría del Entities Editor

**Fecha:** 2026-09-12 · **Nota global: 5.7 / 10** -> **8.8 / 10** tras la ronda de arreglos
(seccion 5 al final: que se hizo, medido).

Siete paneles, 7 740 líneas en 25 ficheros, 96 tests en 7 fixtures. Todo lo que sigue está
**medido** — recuento de campos por reflexión sobre los tipos cargados, el catálogo de 28
monstruos leído del asset, y la UI interrogada en una partida en marcha a 1600x800 — no deducido
de la lectura del código.

---

## 0. El resumen en una frase

El editor **coloca y anima** entidades muy bien, y **no las autora**. Nueve de los diecinueve
campos de `MonsterDefinition` no tienen ninguna UI, incluido el bloque `aiTuning` entero
(diecinueve campos más) y `coinReward`, que es el grifo de monedas de toda la economía. Y de sus
cuatro herramientas globales, Deshacer cubre **una** de las quince operaciones que mutan algo.

---

## 1. Puntuación por panel

| Panel | Nota | Lo que la baja |
|---|---:|---|
| **Tools** | **3.5** | Undo cubre 1 operación de ~15; Save escribe TODO el proyecto; Reload no relee del disco |
| **Categories** | **4.5** | Heurística sobre el texto de la clave; `faction` ya es dato vivo y no se usa; no hay pestaña «Todas» |
| **Picker** | **7.0** | Bien resuelto; reconstruye cada ranura por pulsación; no distingue lo ya colocado |
| **Add / Remove** | **6.0** | Cuatro modos reales; Rename re-clava una clave a la que apuntan otros ficheros, sin confirmar |
| **Properties** | **4.0** | 9 de 22 campos de `EntityStats` editables, 8 ausentes; 9 de 19 de `MonsterDefinition` sin UI |
| **Animation** | **8.0** | El mejor panel del editor; estaba desincronizado del workspace (arreglado hoy) |
| **Timeline** | **6.5** | Idea correcta y bien avisada; alcance estrecho y sin deshacer |

### Transversales

| Aspecto | Nota | Medida |
|---|---:|---|
| Contrato de editor | **9.0** | `EditorName`, `IAllowsPlayerMovement`, `IProvidesWorkspaceState`, zoom, chrome, tutorial: todo presente |
| Deuda de tema | **8.5** | 12 literales `new Color(` en 6 ficheros — de las más bajas del proyecto |
| Persistencia de workspace | **8.0** | Tras el arreglo de hoy; era **3.0** |
| Persistencia de colocaciones | **8.0** | Fichero propio, autosave, guarda anti-borrado, ida y vuelta con tests |
| Cobertura de tests | **7.0** | 96 tests, pero ninguno sobre Properties ni sobre el desajuste de paneles |
| Descubribilidad | **4.5** | El tutorial enseña **F5**, tecla retirada el 2026-09-05 |

---

## 2. Panel por panel

### 2.1 Tools — 3.5

Cuatro botones: Undo, Redo, Save, Reload.

- **`_undo.Record` se llama desde UN solo sitio** (`EntitiesRuntimeEditor.SelectionFx.cs:276`), y
  es *arrastrar una entidad colocada*. No graban: colocar, borrar, cualquier edición de stat, la
  lista de auto-cast, crear, duplicar, renombrar, el timeline, ni el origen de hechizo. La pila es
  de 64 entradas y casi siempre tiene cero. **Nada en pantalla lo dice** — dos botones que parecen
  cubrir el editor y cubren un gesto. **2/10.**
- **`Save` es `AssetDatabase.SaveAssets()`**, que escribe **todo lo que esté sucio en el
  proyecto**, no las definiciones de este editor. CLAUDE.md ya registra por qué eso importa: es la
  llamada que, con el estado de memoria corrupto del incidente de `SkillTree`, habría volcado la
  corrupción sobre ficheros buenos. El botón dice una cosa y hace otra más grande. **4/10.**
- **`Reload` no recarga.** Es `RefreshPicker()`: vuelve a listar el catálogo **desde memoria**. Y
  «un domain reload NO recarga assets», así que un autor que edite un `.asset` por fuera y pulse
  Reload no obtiene nada, sin aviso. **3/10.**

### 2.2 Categories — 4.5

Cuatro pestañas: Hostiles / Neutrals / Specials / Players.

- **La clasificación es una heurística sobre el TEXTO de `monsterKey`** — busca `boss`, `special`,
  `vendor`, `merchant`, `guard`, `civilian`, `neutral` — y el propio código lo admite
  («Heuristic until Python neutrals/specials JSONs are imported»). Medido sobre los 28 shipped:
  **21 Hostiles / 6 Neutrals / 1 Specials, con 1 discrepancia** —
  `barbol_brother_felipondor` autora `faction: NEUTRAL` y cae en **Hostiles** porque su clave no
  contiene ninguna de las palabras.
- Y `stats.faction` **ya no es un campo inerte**: desde la auditoría de IA lo lee
  `EntityFaction.SideOf` y decide quién pelea con quién. La pestaña debería leer el mismo dato que
  el juego; leer el nombre es tener dos respuestas a la misma pregunta.
- **No hay pestaña «Todas»**, así que el catálogo completo no se puede ver de una vez, y una
  entidad mal clasificada es invisible salvo que adivines en qué pestaña cayó.
- El estado visual de la pestaña activa es claro y correcto.

### 2.3 Picker — 7.0

- Búsqueda por **nombre y clave**, sin distinguir mayúsculas, y el contador del estado dice cuántas
  coinciden. Correcto.
- Los iconos se resuelven por entidad y se tiñen con el **tint propio de la entidad**, así que los
  gemelos oscuros se distinguen de un vistazo. Buen detalle.
- Arrastrar una ranura al mapa la coloca. Es el gesto natural y está.
- **Reconstruye TODAS las ranuras en cada pulsación de tecla** (`RefreshPicker` destruye y recrea).
  Hoy son 28 entidades y no se nota; es la misma forma que costó 213 ms por tecla en el editor de
  Controles y 3.5 s en el de Items. No es un defecto ahora, es una deuda que escala con el
  catálogo.
- **No distingue lo que ya está colocado en el mundo.** El panel que elige qué poner no dice qué
  hay puesto.

### 2.4 Add / Remove — 6.0

- Cuatro modos reales (Add, Remove, Add on System, Confirm) más Duplicate y Rename, y un único
  campo de texto que alimenta los tres verbos — decisión documentada y correcta («escribe un
  nombre, luego di qué nombra»).
- **Delete no se restaura del workspace**, deliberadamente, igual que Buildings y Tile. Bien.
- **Rename re-clava la `monsterKey` sin confirmación**, y esa clave es a lo que apuntan
  `assignments.json` del FSM (`by_archetype`), las listas de oleadas de los spawners y los ficheros
  de colocación. Re-clavar es una operación de refactor disfrazada de campo de texto.
- El texto de ayuda es estático («Select a mode then click on the map») y no cambia con el modo,
  aunque el estado de la barra inferior sí.

### 2.5 Properties — 4.0

Seis secciones: Identity / Stats / AI / Spawn / Auto-Cast / Assets. Es el panel más grande y el
más incompleto.

**`EntityStats`: 22 campos.** 9 editables, 5 de solo lectura, **8 ausentes**:

```text
editables : hp speed chasingSpeed defense meleeDamage meleeRange meleeCooldown
            aggroRange attackWindupSeconds
solo lee  : power spawnCount spawnPadding spawnMargin faction
ausentes  : damageDuration damageStopProbability deathDisappearTime
            feetWidthFactor feetHeightFactor chatRange resistances statusImmunities
```

**`MonsterDefinition`: 19 campos, 9 sin ninguna UI**:

```text
level  levelScaling  levelHpGrowth  aiTuning  xpReward  coinReward
lootTable  chatPersona  vendorConfig
```

Tres de esos duelen de verdad:

- **`aiTuning` no existe en la interfaz, y tiene 19 campos propios.** Es el bloque que la auditoría
  de IA hostil creó y llenó para los doce hostiles: `dodgeChance`, `desiredRange`, `fovDegrees`,
  la memoria de visión, la correa. Se autora desde el Inspector o no se autora.
- **`coinReward` no existe en la interfaz.** Es el grifo de monedas de toda la economía, el campo
  que la auditoría económica creó porque **no había ninguno**.
- **`faction` es de solo lectura** aunque desde la auditoría de IA decide quién pelea con quién,
  quién suelta botín y quién es intocable.

Lo que sí está bien: **la sección Auto-Cast es editable y validada** — desplegable contra el
catálogo de hechizos, añadir/cambiar/quitar, y una clave desconocida se rechaza con un motivo. Es
la mejor parte del panel y el modelo que las otras secciones deberían seguir.

Y el formulario **no tiene búsqueda**, en un panel que ya son seis secciones y va a crecer.

### 2.6 Animation — 8.0

El mejor panel del editor, y el único construido contra la ruta real del juego.

- El rig lo monta **`EntityAnimationBinder.ApplyLoadout`**, el bind del propio juego, así que lo
  que se ve es lo que se jugará — incluido **el respaldo cuando un estado no tiene arte, dicho con
  palabras** (medido en `red_dragon`: chase muestra walk; damage, death y recover muestran idle).
- Pausa, paso a paso, scrub, tira de fotogramas, línea de suelo, piel de cebolla, y los **tres
  multiplicadores de velocidad por separado** (entidad x estado x variante) porque el producto
  esconde cuál manda y se autoran en tres sitios distintos.
- El orden de los fotogramas **no es editable, a propósito**: los importadores reescriben las
  listas enteras en cada wave.
- **Nuevo hoy**: el selector del origen del hechizo (ver `CLAUDE.md`).
- **Defecto encontrado y arreglado hoy**: el panel estaba **desincronizado del workspace**. Medido
  en vivo — `_animPanelOpen` true, la cámara de vista previa y su RenderTexture funcionando, el
  botón del menú iluminado, y el GameObject del panel **inactivo**. El autor no veía panel, el menú
  decía que había uno abierto, y su siguiente clic lo CERRABA: dos clics para recuperar un panel
  que nunca se vio. Causa: `EditorWorkspaceService.ApplyNow` restaura el estado abierto/cerrado de
  cada `DraggablePanel` escribiendo el GameObject directamente, sin pasar por `SetDropdownOpen`.
  Afectaba a los siete paneles; los dos con un coste detrás (la cámara de Animation, Timeline) se
  quedaban además funcionando para algo invisible.

### 2.7 Timeline — 6.5

- **Los dos relojes en un solo eje.** El hechizo decide cuándo dispara, la animación cuánto dura, y
  hasta este panel nada los reconciliaba. Poner las dos pistas sobre el mismo eje es exactamente lo
  que hace que un wind-up que acaba antes del disparo *se vea* mal.
- **Avisa antes de escribir**: cuenta cuántas entidades comparten ese hechizo, porque un
  `SpellDefinition` lo comparten todos los que lo lanzan. Ese aviso es el detalle que separa una
  herramienta de un editor de campos.
- Todas las escrituras van por `CommitDefinitionEdit` — el mismo sello que el resto. Pero **no son
  deshacibles**, como todo lo demás salvo el arrastre.
- Alcance estrecho: solo alcanza variantes que reservan un hechizo.

---

## 3. Transversales

- **El tutorial enseña `F5`**, y los toggles de editor se retiraron el 2026-09-05 — se entra por
  Escape. No es solo aquí: **FSM dice F12, Inventory F6 y Tile F8** en sus propias superposiciones.
  Es la misma clase de defecto que los cinco consejos de la pantalla de carga que nombraban teclas
  muertas.
- **Deuda de tema baja**: 12 literales `new Color(` en 6 ficheros, contra los 31 de un solo fichero
  del editor de Hechizos.
- **Contrato de editor completo**: nombre, movimiento del jugador, workspace, zoom de cámara,
  chrome arrastrable, tutorial. Nada que reprochar.
- **96 tests en 7 fixtures**, y cubren lo que se puede cubrir sin pintar: autoría de catálogo,
  auto-cast, persistencia de colocaciones, ida y vuelta del serializador, la vista previa. **No hay
  ninguno sobre el panel de Properties**, que es el que más campos mueve.

---

## 4. Qué haría, por orden de rentabilidad

1. **Exponer `aiTuning` y `coinReward` en Properties.** Son 20 campos que hoy solo existen en el
   Inspector, en el editor cuyo trabajo es autorar entidades.
2. **Grabar en la pila de deshacer todo lo que muta.** Hoy es 1 de 15. Cada edición ya pasa por
   `CommitDefinitionEdit`: ahí hay una costura única donde grabar.
3. **Clasificar las pestañas por `faction`, no por el texto de la clave**, y añadir «Todas».
   Cierra la discrepancia medida y quita una heurística que el propio código llama provisional.
4. **Que `Save` guarde solo lo de este editor** (los assets que ensució), no todo el proyecto.
5. **Que `Reload` recargue de verdad** — reimportar las definiciones del disco, no relistar memoria.
6. **Confirmación en Rename**, que es un refactor de una clave a la que apuntan tres subsistemas.
7. **Actualizar los cuatro tutoriales** que enseñan teclas F retiradas.
8. **Hacer editables los 8 campos ausentes de `EntityStats`** — `resistances` y `statusImmunities`
   los primeros, que son los que deciden si un hechizo elemental sirve contra ese monstruo.

---

## 5. Lo que se arregló, y cómo quedó — medido

Ronda de arreglos el mismo día, atacando de la nota más baja hacia arriba.

| Panel / aspecto | Antes | Después | Qué cambió |
|---|---:|---:|---|
| **Tools** | 3.5 | **9.0** | Undo cubre todo; Save escribe solo lo suyo; Reload reimporta de verdad |
| **Categories** | 4.5 | **9.0** | Clasifica por `faction` y `bossDefinition`; pestaña «All», y es la de arranque |
| **Picker** | 7.0 | **8.5** | Marca y cuenta lo ya colocado en el mundo |
| **Add / Remove** | 6.0 | **8.5** | Rename informa de las referencias y pide segunda pulsación; ayuda por modo |
| **Properties** | 4.0 | **9.0** | 60 filas en 8 secciones; `aiTuning` y `coinReward` completos; filtro |
| **Animation** | 8.0 | **9.0** | Desincronización con el workspace cerrada |
| **Timeline** | 6.5 | **8.0** | Deshacer gratis: ya escribía por `CommitDefinitionEdit` |
| Descubribilidad | 4.5 | **9.0** | Siete tutoriales dejaron de enseñar teclas muertas, con guard |
| Cobertura de tests | 7.0 | **9.0** | 96 → 113 en Entities; guards de cobertura de campos y de undo |
| Persistencia de workspace | 3.0 → 8.0 | **9.0** | Reconciliación tras restaurar |

### Deshacer: de 1 de 15 a todas

El seam ya existía — cada mutación termina en `CommitDefinitionEdit` — así que el arreglo fue
**una instantánea JSON de la definición** ahí, no quince comandos por campo. `JsonUtility`
round-trip captura lo que un comando escrito a mano habría olvidado: un `assetConfig` anidado,
una lista redimensionada, un campo de struct que nadie enumeró.

Verificado en vivo sobre `dark_dwarf`: 3 ediciones (hp, dodgeChance, coinReward) → `undoCount=3`
→ 3 undo → **restauración exacta** → 3 redo → los tres valores vuelven → 3 undo → limpio.

**Un fallo real encontrado al probar, no leyendo:** la semilla estaba en `CurrentEditableMonster`,
que las filas de stats **nunca llaman** (capturan `def` directamente). Medido: 3 ediciones, **2
pasos de undo**, y el HP no volvía. La semilla vive ahora en `ShowMonsterProperties`, el último
momento antes de que una edición sea posible.

### Save y Reload

`SaveAssetIfDirty` por asset en vez de `SaveAssets()`: un botón dentro de un editor escribe el
trabajo de ese editor, no todo lo sucio del proyecto. Verificado: 1 asset registrado tras una
edición. Y `Reload` reimporta del disco, pero **avisa antes**: la primera pulsación con cambios
sin guardar arma y explica, la segunda ejecuta — el mismo gesto que rescata de un error no puede
ser el que tira una sesión buena.

### Categories por dato

`MatchesCategory` lee `stats.faction` y `bossDefinition` (una referencia a objeto, no una
subcadena). Medido sobre los 28 shipped: **All 28, Hostiles 20, Neutrals 7, Specials 1, 0
discrepancias**. `barbol_brother_felipondor` — `faction: NEUTRAL` — pasó de Hostiles a Neutrals.

### Properties completo

De 6 a 8 secciones, **60 filas**. `aiTuning` entero (19 diales agrupados por decisión: adquirir,
perseguir, mantener distancia, huir, esquivar), `coinReward` con su contrato de tres estados, los
8 campos ausentes de `EntityStats`. Y un aviso que solo se puede dar aquí: si el set FSM del
monstruo **no declara `DodgeState`**, los diales de esquiva no hacen nada — preguntado a
`FSMRuntimeFactory.AllowsState`, no adivinado por el nombre del set.

Filtro verificado en vivo: sin filtro **60 filas / 8 secciones**, `dodge` → **5 / 1**, `coin` →
**1 / 1**, `zzzz` → **0 / 0** (sin cabeceras huérfanas), y vuelve a 60.

**Segundo fallo encontrado probando:** el filtro asumía que la cabecera era el hijo 0 del
contenedor de filas. `MakeFormSection` devuelve el **Body** y la cabecera es su *hermana*, así que
el filtro conmutaba el Body como si fuese una fila y **no tocaba ninguna**. Se ve en cuanto se
cuentan filas en un editor vivo; no se ve leyendo.

### Rename con red

Informa de lo que apunta a la clave y exige segunda pulsación. Medido sobre `barbol`:

```text
"an FSM assignment, 13 spawner wave entries and 1 placement on the map"
1er Rename -> no renombra, arma y explica
```

### Teclas muertas

El guard nuevo encontró **siete** overlays enseñando toggles F retirados (Entities F5, FSM F12,
Inventory F6, Tile F8, Items F7, Spawners F3, Time & Weather F2). El primer patrón era demasiado
laxo y marcaba tres `ToString("F2")` como tutoriales — un guard que da falsos positivos es un
guard que se acaba desactivando, así que exige la coma de la tupla.

### Lo que sigue abierto

- **El Picker reconstruye sus ranuras en cada pulsación.** 28 entidades hoy; es deuda que escala,
  no un defecto. Virtualizar ahora sería maquinaria que no guarda nada.
- **`lootTable`, `chatPersona`, `vendorConfig` se muestran por nombre pero no se eligen.** Elegir
  un asset pide un selector de assets; un campo de texto con una ruta es la forma que deja de
  resolver en silencio.
- **Crear y Duplicar no son deshacibles**, a propósito: crean ficheros `.asset`, y deshacer eso es
  borrar del disco.
- **`resistances` y `statusImmunities` se muestran, no se editan.** Son listas de structs y
  necesitan un editor de filas propio.
