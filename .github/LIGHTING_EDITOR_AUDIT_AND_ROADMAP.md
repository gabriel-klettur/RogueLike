# Editor de Iluminación (Ctrl+F3) — auditoría y hoja de ruta

> Auditoría completa del editor de iluminación en runtime de Valkur, con puntuación rigurosa
> 0–10 por área y el plan para llevarlo a calidad profesional.
>
> Fecha: 2026-08-25 · Método: 5 auditorías paralelas de solo lectura (ciclo de vida y cableado,
> paridad UI/UX, superficie funcional, persistencia y seguridad de datos, robustez y tests), cada
> una verificada adversarialmente por una segunda pasada, más verificación manual en el juego en
> marcha de los hallazgos que sostienen las conclusiones.

## Resumen ejecutivo

**Puntuación global: 4.1 / 10.**

El editor abre, coloca luces y guarda. El esqueleto de ciclo de vida es una copia fiel del
contrato de los editores hermanos y su panel de día/noche es la mejor pieza del conjunto. Pero
es **el editor más pequeño del proyecto por mucho** — 1976 líneas en 8 ficheros, frente a 8890 de
Partículas o 9652 de Edificios — y esa diferencia no es elegancia: es funcionalidad que no está.

Tres cosas hay que saber antes que ninguna otra.

### 1. Todas las luces autoradas del juego están 150–200 tiles de donde dicen estar

`WorldLightLoader._zoneManager` es un `[SerializeField]` que **nadie asigna nunca**. No hay
`SetZoneManager` en el proyecto, el bootstrap solo llama a `SetCatalog`
(`GameplaySceneSetup.Systems.cs:261-276`), y ninguna escena ni prefab lleva el componente. Los
cargadores hermanos tienen todos el respaldo que a este le falta —
`BuildingLoader.cs:134-136`, `SpawnerInstanceLoader.cs:79-81`,
`ParticleInstancesLoader.Positioning.cs:50-54` hacen `FindObjectOfType<ZoneManager>()`.

Consecuencia: `zoneOffset` vale siempre `(0,0)` y el campo `zone` del JSON es decorativo.

Medido en el juego corriendo:

| | valor |
|---|---|
| `loader._zoneManager` | **NULL** |
| ¿Hay un `ZoneManager` en escena? | **Sí** — un `FindObjectOfType` lo habría encontrado |
| `gridOffset` de `zone_100_50` | (200, 50) |
| `gridOffset` de `lobby` | (150, 50) |
| Luz id=1 (`zone_100_50`, rel 1323/457) | aparece en **(41.3, 34.7)**, debería estar en **(241.3, 84.7)** |

Las 10 luces autoradas están desplazadas: las 4 de `zone_100_50` por (+200, +50), las 6 de
`lobby` por (+150, +50).

Esto **corrige una interpretación previa de la auditoría de día/noche**, que anotó que "las 10
luces autoradas están en otra zona del mapa" y lo atribuyó a decisiones de autoría. No lo era:
41.3 + 200 = 241.3 cae justo en el borde este del pueblo. Las luces siempre estuvieron destinadas
a alumbrar el pueblo. La oscuridad que se resolvió añadiendo luces derivadas a las farolas la
causaba este bug.

Es la forma de defecto **#2** del incidente `.github/incidents/SPAWNER_COORDINATE_SPACE_DRIFT.md`,
literal, en un subsistema que ese incidente decía explícitamente que había que revisar.

### 2. El primer Ctrl+S hace el daño irreversible

Con `_zoneManager` nulo, `ResolveZoneAt` devuelve `zoneId = ""` y `AppendInstance` lo escribe. El
fichero que se envía hoy lo generó el importador de Python, no el editor — se nota porque sus
arrays `color` van partidos en varias líneas mientras `AppendOverridesBody` los emite en línea —
así que las coordenadas zona-relativas autoradas **siguen intactas en disco**.

El primer guardado desde el editor reescribe los diez registros con `"zone": ""` y coordenadas en
el espacio de offset cero. Las posiciones no cambian en pantalla, así que nada parece distinto —
pero la atribución de zona desaparece, y cablear el `ZoneManager` después ya no puede reconstruir
la colocación original. A diferencia de los spawners, cuyo `id` de texto permitió reparar 27 de
27, el `id` de una luz es un entero pelado: después de ese guardado no queda nada de donde
reconstruir salvo el historial de git.

### 3. No hay ninguna guarda anti-borrado, y el borrado total se alcanza en tres clics

`SaveAll` llega a `WriteRawJson` (`WorldLightLoader.cs:532`) sin lectura previa, sin contar los
registros en disco, sin negarse a escribir vacío y sin comprobar proporciones. `DoSave`
(`LightingRuntimeEditor.Save.cs:12-21`) tampoco añade ninguna.

Los hermanos sí las tienen:

```
[ParticlesEditor] ABORTING save — scene holds 0 particle instances but the file
holds 188. File NOT written.
```

El de Edificios aborta ante un colapso de posiciones y ante un recuento decreciente. El Editor de
Mapas tiene **4 capas de backup** por el incidente de las 38 zonas perdidas. Y el propio Editor de
Mapas documenta este peligro **para este mismo fichero**: guarda su volcado con
`if (loader == null || loader.ActiveLightCount == 0) return;` y el comentario *"Flushing that
state would serialise an empty array over a perfectly good light_instances.json."*

El Ctrl+S del editor de iluminación no.

Se reproduce verbatim durante esta auditoría: se encontró el editor con `_activeLights` = 3, las
tres derivadas de edificios y **ninguna autorada**, mientras el fichero tenía las 10. Un Ctrl+S
en ese momento escribe `[\n\n]\n` — 5 bytes — y muestra el brindis *"Saved 0 light instance(s)"*.
Es el accidente que dejó `particles_instances.json` en 4 bytes.

Peor: `FlushLightEditsForOutgoingSlot` se protege con `ActiveLightCount == 0`, pero
`ActiveLightCount` cuenta `_activeLights.Count`, que **incluye las luces derivadas no
persistentes** que `SaveAll` luego descarta. Un mundo cuyas luces autoradas no llegaron a
aparecer pero cuyas farolas sí, pasa la guarda con un recuento distinto de cero y escribe cero
registros — al cambiar de mapa, en silencio.

## Escala de puntuación

| Nota | Significado |
|---|---|
| 0 | No existe, o existe y no hace nada |
| 1–2 | El código existe pero es inerte o incorrecto |
| 3–4 | Funciona parcialmente, con defectos visibles |
| 5–6 | Correcto pero anodino |
| 7–8 | Bueno, nivel profesional |
| 9–10 | Referencia |

## Puntuación por área

| Área | Auditoría | Tras refutación | Peso |
|---|---|---|---|
| 1. Persistencia y seguridad de datos | 3 | **3** | 25 % |
| 2. Superficie funcional | 4 | **4** | 25 % |
| 3. Paridad UI/UX | 5 | **4.5** | 20 % |
| 4. Robustez y tests | 3 | **4** | 20 % |
| 5. Ciclo de vida, cableado e input | 5 | **6** | 10 % |
| **Global** | | **4.1** | 100 % |

Las cinco áreas fueron refutadas por una segunda pasada que abrió cada fichero citado. Las cinco
salieron **`partly-holds`**: la disciplina de citas fue alta, y las correcciones afectaron sobre
todo a razonamientos, no a hechos. Dos puntuaciones se movieron: ciclo de vida subió a 6 (el
razonamiento tenía una mitad rota, pero la evidencia superviviente sostiene más nota), robustez
subió a 4 (el 3 se juzgó algo severo) y paridad bajó a 4.5.

## 1. Persistencia y seguridad de datos — 3 / 10

Lo bueno es heredado, no propio: la escritura pasa por `WorldStreamingFileRepositoryBase`
(temporal + `File.Replace` + un `.bak` de una generación).

**El enrutado por slot de mapa SÍ funciona**, y esto corrige una afirmación preliminar de esta
misma auditoría que decía lo contrario. `JsonFileLightInstanceRepository` declara
`IsMapSlotAware => true` (`:15`) con el comentario que lo justifica, y está cubierto por
`WorldContentPerSlotRoutingTests` (`[TestCase("Lights")]`) y `JsonFileLightInstanceRepositoryTests`.
La Fase A multi-mapa aterrizó de verdad para las luces. Editar las luces de un mapa no puede
pisar el fichero de otro. Es la parte más fuerte del área.

| Defecto | Qué pierde el usuario |
|---|---|
| `_zoneManager` muerto | Las 10 luces del juego, desplazadas 150–200 tiles |
| El primer guardado escribe `zone: ""` | La atribución de zona, de forma irrecuperable |
| Cero guardas anti-borrado | El fichero entero, en tres clics |
| `ActiveLightCount` cuenta derivadas | El slot saliente al cambiar de mapa, en silencio |
| Sin flag de sucio, sin autoguardado, sin guardar al cerrar | Coloca veinte antorchas, pulsa Esc: las veinte |
| `SpawnFromData` descarta registros con preset desconocido | Renombra una clave del catálogo y el siguiente guardado borra esas luces. Partículas hace lo contrario a propósito: reemite verbatim lo que no pudo crear |
| El escritor no cubre todos los campos que el lector acepta | Un `"falloff"` autorado a mano se ignora al cargar y se borra al guardar |
| `.bak` dentro de `Assets/` | Unity lo importa y genera `.meta`; viola la regla de CLAUDE.md de que los backups no van en `Assets/` |

**Viñeta positiva que conviene declarar:** el culling por viewport **no** causa pérdida.
`SaveAll` filtra por `inst.go == null`, nunca por `activeSelf`. Guardar a mediodía o con la cámara
en una esquina conserva todas las luces.

## 2. Superficie funcional — 4 / 10

Es una herramienta de **colocación** competente y nada más. Tres modos (Select / Spawn / Delete),
unas 20 acciones distintas, y el subconjunto de colocación está completo: spawn, arrastrar, borrar,
enfocar, guardar.

**El panel de día/noche es la mejor pieza del editor**: 11 controles, todos fachadas correctas
sobre el `DayNightCycle` vivo sin estado duplicado, con un `_suppressCycleEvents` que evita bien
el bucle UI↔ciclo.

Lo que no está:

**No hay ninguna realimentación visual en el mundo.** Un grep en toda la carpeta no encuentra
`LineRenderer`, ni gizmo, ni componente de contorno, ni enganche a `endCameraRendering`. El único
`Outline` es chrome uGUI de la barra de menú. Los hermanos envían todos su renderer de contorno
dedicado: `BuildingOutlineRenderer`, `ParticleEmitterOutlineRenderer`, `EntityOutlineRenderer`,
`SpawnerOutlineRenderer`.

La selección es una prueba de distancia al centro con radio **0.6 unidades de mundo** — un punto
invisible de 19 píxeles en el centro de un resplandor cuyo preset Torch mide 4 unidades. Hay que
acertar dentro de un séptimo del radio visible, sin nada en pantalla que indique dónde está ese
centro. Y a la hora de inicio por defecto del ciclo (0.35, dentro de la ventana diurna de luces
apagadas) **todos los GameObjects de luz están desactivados**: no hay nada en pantalla. Se borra a
ciegas.

**No se puede editar ningún override por instancia.** `OverrideLight` tiene **cero llamantes** en
todo el proyecto, y el panel de propiedades es un volcado de `StringBuilder` de solo lectura. Los
10 registros que se envían llevan `overrides`, y desde el editor solo se pueden perder.

**No se puede crear, duplicar ni editar un preset.** El editor de Partículas edita presets en vivo.

## 3. Paridad UI/UX — 4.5 / 10

Cosas que **solo** este editor tiene, y que merecen conservarse:

- Reloj vivo con descriptor de fase dentro de un panel, refrescado cada frame.
- Editor de ventana temporal con envoltura de medianoche — nada más en el proyecto edita un rango
  horario circular.
- **Es el único editor que llama a la fábrica de paneles compartida.** Once editores llevan copia
  privada de `MakeDrop`; este delega, y por eso hereda gratis los arreglos de la fábrica.
- Sincronía bidireccional por frame con guarda de supresión de eventos.
- Ctrl+Z / Ctrl+Y / Ctrl+S los tres enlazados — Partículas no tiene ninguno.

Defectos:

**Dos paneles se solapan 280×150 px** a la resolución de referencia del canvas. La línea de estado
es el último hijo del panel de Presets y cae **dentro de la banda tapada**. Todos los mensajes del
editor — *"Spawned torch at (..)"*, *"Pick a preset before spawning."*, *"Saved 9 light
instance(s)"* — se escriben en una etiqueta que el autor no puede ver hasta que arrastra un panel.
Partículas resuelve el mismo apilamiento explícitamente.

**Dos sliders se dibujan a 100×100 px dentro de una fila de 264×16** y se solapan entre sí en una
banda de 37 px. En esa banda gana el raycast el último hermano, así que apuntando a "Day length"
se arrastra en realidad el suelo global de brillo nocturno, y el número de day-length no se mueve.
*(Verificado construyendo la UI real en el Editor y forzando dos pasadas de layout.)*

**Los dos toggles pintan el inverso de su estado.** `ApplyToggleBtnStyle` solo se invoca desde sus
propios manejadores de clic, nunca desde `Activate`, así que ambos abren en gris —que significa
OFF— mientras los dos sistemas están ON. El primer clic no produce ningún cambio de color.

**El toggle "Ambient (Global Light)" está mal etiquetado y es inerte de día.** No toca el Global
Light: escribe `DayNightCycle.MinIntensity`, que se aplica como `Mathf.Max(intensity, min)`. En la
banda de día la intensidad ya es 1.0, así que bajar el suelo a 0 no cambia nada. El autor pulsa,
el botón se apaga, el estado dice "OFF" y el mundo sigue igual de brillante. Su propósito
declarado —depurar la contribución de las luces puntuales— es justo el caso en que falla.

Y puede **sobrescribir en silencio el suelo nocturno autorado**: `_cachedDayLightIntensity` nace a
0 y la rama de encendido escribe `0.20f` si la caché es 0. El valor que se envía es `0.08f`. Dos
veces y media más brillante, para el resto de la sesión, sin entrada de undo.

**Desajuste de rango de velocidad con el editor F2.** El 100× de F2 da 36 s/día, por debajo del
mínimo de 60 s del slider de este editor. Abre Ctrl+F3 después y el mando y su número se
contradicen; el primer toque escribe ≥60 y tira la velocidad.

**`Deactivate` no restaura ninguno de los tres estados globales que el editor cambia** —
`Paused`, `MinIntensity` y `PointLightsEnabled`. Pausa el ciclo para componer una escena nocturna,
cierra con Ctrl+F3, y el mundo ya no vuelve a cambiar de hora en toda la sesión, sin mensaje.

## 4. Robustez y tests — 4 / 10

No quedan trampas de componente duplicado: se comprobó cada `AddComponent` de la carpeta contra el
conjunto `[DisallowMultipleComponent]`. Tampoco hay corrutinas, ni suscripciones desbalanceadas,
ni estado estático sin resetear.

Lo que sí hay es **un sistema de undo que corrompe lo que el siguiente guardado escribe**:

| Secuencia | Resultado |
|---|---|
| Borrar → Ctrl+Z → Ctrl+Y | La luz sigue ahí. Estado y consola dicen "Redo: Delete …" como si hubiera funcionado. Un Ctrl+Z más y aparece una **segunda** luz idéntica |
| Spawn → Ctrl+Z → Ctrl+Y → Ctrl+Z | Queda una luz que el undo ya no puede quitar. Cada Ctrl+Y añade otra copia encima |
| Borrar una luz con overrides → Ctrl+Z | Vuelve con el color plano del preset y un id nuevo. El ajuste desaparece sin mensaje |

El mecanismo que oculta los tres: `UndoStack.Undo` y `Redo` **se tragan toda excepción con un
`catch` vacío**, mientras `DoUndo`/`DoRedo` informan éxito incondicionalmente.

**La pila de undo no se limpia nunca** — ni al activar, ni al desactivar, ni al cambiar de slot de
mapa. Cuatro editores hermanos limpian la suya al desactivar, por una razón documentada. Autora
luces en el mapa A, cambia al B, pulsa Ctrl+Z: la lambda de undo del borrado inyecta la luz del
mapa A en el mapa B, en las coordenadas del A. Ctrl+S la escribe en el fichero del B.

**Cobertura de tests: una sola fixture propia**, `LightingEditorUIBuilderTests`, añadida el
2026-08-25 al arreglar el crash de los paneles. Nada ejercita `SaveAll`, `ComputeWorldPosition`,
`ResolveZoneAt` ni un ciclo guardar→cargar. El incidente de los spawners dejó justo las dos
fixtures que aquí faltan: una de ida y vuelta de 25 ciclos y otra de integridad del fichero
enviado. **La segunda estaría en rojo hoy sin arrancar el juego.**

## 5. Ciclo de vida, cableado e input — 6 / 10

Lo mejor del editor. El esqueleto es de manual: implementa `IGameEditor`, se registra en `Start` y
se da de baja en `OnDestroy`, enruta el toggle por `ToggleExclusive` con respaldo autónomo,
resuelve su hotkey por la API sin estado `EditorHotkeyBindings.WasPerformedThisFrame` —inmune al
bug de la InputAction zombi tras recompilar—, arbitra Ctrl+F3 frente a F3 simétricamente con el
editor de Spawners, y empareja `DetachFollow()` con `ReattachFollow()`. **Cero estado estático
mutable**, así que el riesgo de Domain Reload OFF sencillamente no aplica.

**Corrección a una afirmación preliminar:** el toggle **no** está hardcodeado; pasa por
`EditorHotkeyBindings` como el de todos los hermanos. Lo hardcodeado es la exigencia de **Ctrl**, y
está documentado a propósito en tres sitios, incluido el panel de Opciones, que pinta la fila como
`"Ctrl + F3 (fixed)"` de solo lectura. Es honesto, no roto en silencio.

Lo que sí rompe para quien reasigna teclas es **la puerta OR heredada**, y no es específico de este
editor: `WasPerformedThisFrame` devuelve `nuevoSistema || LegacyKeyDown`, y `LegacyKeyCode` fija
`ToggleLighting => KeyCode.F3` sin mirar `GameSettings`. Reasignas y **la tecla vieja nunca se
libera**.

Cuatro defectos visibles:

- Es el único editor de colocación cuyo gesto de clic izquierdo no está cubierto por
  `ISuspendsPlayerCombat`: **cada luz colocada lanza también una bola de fuego**, y cada Ctrl+S
  hace un dash.
- Conserva un `if (Mouse.current == null) return;` en `HandleMapInteraction` — la línea exacta que
  Edificios, Objetos y Partículas borraron cada uno con un comentario explicando que *"suprimía
  toda la interacción de mapa bajo el bug"*.
- El `try/catch` de `Activate` vuelve sin decirle a `GameEditorManager` que falló, dejando al
  gestor apuntando a un editor que no está activo, el HUD escondido, y un canvas huérfano por
  reintento.
- Tiene caja de búsqueda estando marcado `IAllowsPlayerMovement` — trampa que la documentación del
  propio proyecto nombra.

## Hoja de ruta

Ordenada por daño evitado. La fase 0 no es opinable.

### Fase 0 — Parar la pérdida de datos

1. **Asignar el `ZoneManager`.** Copiar el respaldo que ya tienen los tres cargadores hermanos:
   `if (_zoneManager == null) _zoneManager = FindObjectOfType<ZoneManager>();`. Con eso las 10
   luces vuelven a su sitio sin tocar los datos.
2. **Verificar antes de tocar disco.** Antes de arreglar nada, comprobar que el fichero enviado
   sigue en el espacio zona-relativo original (lo está: lo escribió el importador de Python). Un
   guardado antes del arreglo lo destruye.
3. **Guarda anti-borrado en `SaveAll`**, con la forma que ya usa Partículas: leer el recuento en
   disco, negarse a escribir cero sobre no-cero, negarse ante una caída desproporcionada, y
   registrar un error con la razón. Nunca un brindis de éxito.
4. **Arreglar `ActiveLightCount`** para que cuente solo persistentes, o que
   `FlushLightEditsForOutgoingSlot` filtre por `persistent`.
5. **Reemitir verbatim los registros que no se pudieron crear**, como hace
   `ParticleInstanceSerializer.SerializeRecords`.
6. **Sacar el `.bak` de `Assets/`.**

### Fase 1 — Que el undo deje de corromper

7. Arreglar las tres rutas: redo de borrado no-op, redo de spawn que fuga, y undo de borrado que
   pierde id y overrides. Capturar el `LightInstanceData` completo antes de destruir.
8. Quitar los `catch` vacíos de `UndoStack`, o al menos registrar.
9. Limpiar la pila al desactivar y al cambiar de slot.

### Fase 2 — Que se pueda ver lo que se edita

10. `LightOutlineRenderer` en el mundo: círculo de radio real, contorno al pasar por encima,
    selección distinta. Es lo que convierte el borrado a ciegas en una herramienta.
11. Radio de acierto proporcional al radio de la luz, no 0.6 fijo.
12. Mostrar las luces aunque la ventana diurna las tenga apagadas, mientras el editor está abierto.

### Fase 3 — Paridad y corrección de la UI

13. Resolver el solapamiento de paneles y el de los dos sliders.
14. Pintar el estado inicial de los toggles.
15. Arreglar o retirar el toggle "Ambient": o gatea de verdad el Global Light, o se le cambia el
    nombre a lo que hace.
16. Restaurar en `Deactivate` los tres estados globales.
17. Alinear el rango de velocidad con el editor F2.

### Fase 4 — Lo que falta como herramienta

18. Edición de overrides por instancia — `OverrideLight` ya existe y no lo llama nadie.
19. Flag de sucio, aviso al cerrar, y autoguardado con debounce como el de Spawners.
20. Crear/duplicar presets desde el editor.

### Fase 5 — Blindaje

21. Fixture de integridad del fichero enviado: cada coordenada dentro de la zona que dice.
22. Ida y vuelta guardar→cargar sobre 25 ciclos.
23. Test de que `_zoneManager` se resuelve en runtime.
24. Tests de las cuatro secuencias de undo/redo.
25. Test de que la guarda anti-borrado se dispara.

---

## Informe de la Fase 0 — completada (2026-08-26)

Los seis puntos de la fase 0 están cerrados. Todo lo que sigue está medido en Play Mode contra el
mundo real, no razonado sobre el código.

### 1. Las luces vuelven a su sitio

`WorldLightLoader._zoneManager` es un `[SerializeField]` que nadie asigna. `ComputeWorldPosition`
lo leía directamente, resolvía `null`, y caía al offset cero. Ahora existe
`ResolveZoneManager()`, que cachea el campo y recurre a `FindObjectOfType<ZoneManager>()` — el
mismo respaldo que ya usaban los tres cargadores hermanos. Lo usan tanto `ComputeWorldPosition`
como su inverso `ResolveZoneAt`, que es la mitad que importa: arreglar solo la carga habría hecho
que el siguiente guardado reescribiera las diez luces en el espacio equivocado.

Medido en `MainGameplay`, las diez luces autoradas, `zone_100_50` con `gridOffset` (200, 50):

| Luz | Antes | Ahora |
|---|---|---|
| `Light_1_Torch` | (41.3, 34.7) | **(241.3, 84.7)** |
| `Light_2_Torch` | (24.7, 34.7) | (224.7, 84.7) |
| `Light_9_Magic` | (-38.8, 30.3) | (161.2, 80.3) |

`authored=10 derived=0 unspawned=0` — las diez cargan, ninguna se pierde.

### 2. El fichero estaba intacto, y se verificó antes de tocarlo

Antes de cambiar una línea: 10/10 registros dentro de los límites de su zona, 0 zonas en blanco.
El fichero seguía en el espacio zona-relativo que escribió el importador de Python, así que el
arreglo era reparable sin pérdida. Un guardado ANTES del arreglo lo habría destruido: la luz 1
se habría reescrito como `rel = (41.3, 34.7)` sobre un offset cero y, al recargar con el arreglo
puesto, habría aparecido a 200 tiles del sitio correcto — el error duplicado en vez de corregido.
Hay una instantánea en el scratchpad de la sesión.

### 3. La guarda anti-borrado

`SaveAll(bool force = false)` devuelve `SaveAborted` (-1) en vez de escribir cuando lo que va a
salir no se parece a lo que hay en disco. Verificado con el fichero real de 10 registros:

```
onDisk=10
  write  0 -> REFUSED: the world holds 0 authored lights but the file holds 10.
  write  4 -> REFUSED: ... too large a drop to be an edit.
  write  5 -> ALLOWED      (exactamente la mitad: borrar la mitad es una edición plausible)
  write 10 -> ALLOWED
  write 25 -> ALLOWED
```

Dos detalles deliberados. Un fichero **ilegible no es un fichero vacío**: `CountRecordsOnDisk`
devuelve `int.MaxValue` ante una excepción, porque tratar un fallo de IO como permiso para
sobrescribir convierte un error transitorio en pérdida de datos. Y `LightingRuntimeEditor` ya no
brinda: ante `SaveAborted` el toast dice `Save ABORTED`, que es el único sitio donde creerse un
"guardado" falso cuesta el fichero.

### 4. Contar lo que de verdad se guarda

`ActiveLightCount` incluye las luces derivadas de edificios-farola, que `SaveAll` nunca escribe.
Un mundo cuyas luces autoradas no cargaron pero cuyas farolas sí tiene un `ActiveLightCount` sano
y cero registros que guardar — que es exactamente cómo se cuela un array vacío. Ahora hay
`PersistentLightCount`, `DerivedLightCount`, `UnspawnedRecordCount` y `PersistentLightObjects`.

- `MapEditorManager.FlushLightEditsForOutgoingSlot` gatea con `PersistentLightCount`.
- El panel de instancias lista `PersistentLightObjects` y su cabecera dice
  `N authored lights (+M unspawnable, kept) · K from buildings` en vez de un "N lights spawned"
  que no coincidía con nada.

### 5. Reemisión verbatim

`_unspawnedRecords` conserva los registros que la sesión no pudo convertir en luz — preset
renombrado, zona sin cargar — y `AppendRecordData` los reemite campo por campo. Sin esto, una sola
clave de preset renombrada borraba en silencio todas las luces que la usaban en el siguiente
guardado. `LoadInstances` además registra ahora un `LogError` cuando falta el catálogo, en vez de
volver sin decir nada.

Verificado ida y vuelta: un registro con los seis overrides sale y vuelve idéntico; uno sin
overrides no se inventa ninguno y vuelve con el centinela `-1`.

### 6. El `.bak` fuera de `Assets/`

`StreamingAssets/Lights/light_instances.json.bak` era una copia manual de febrero, sin escritor
en el código (los `.bak` rotatorios de partículas los escribe `AtomicJsonFile`; las luces no
pasan por ahí). Estaba en gitignore y sin trackear, pero Unity lo importaba igual y generaba su
`.meta`. Era byte a byte idéntico al fichero vivo; se movió al scratchpad y se borró el `.meta`.

### Blindaje — adelantado desde la Fase 5

`Tests/EditMode/Game/World/Lighting/WorldLightLoaderPersistenceTests.cs`, 10 tests, todos verdes.
Cubre los puntos 21, 23 y 25 de la Fase 5:

| Test | Qué fija |
|---|---|
| `ShippedLightFile_IsPopulatedAndWellFormed` | El fichero enviado no es un `[]`. Un array vacío parsea perfectamente: solo el recuento lo delata |
| `ShippedLightFile_CoordinatesAreZoneRelative` | Cada coordenada dentro de su zona — la forma de aserción que faltaba en el incidente de deriva de spawners |
| `WorldPositionAndRelCoords_RoundTripExactly` | La COMPOSICIÓN carga∘guarda, no cada mitad por separado |
| `ResolveZoneManager_FindsTheSceneInstanceWhenTheFieldIsUnassigned` | La referencia nula que desplazó todas las luces del juego |
| `MayOverwrite_RefusesAWipeAndADisproportionateDrop` | La guarda salta, y no estorba a un guardado legítimo |
| `CountRecordsOnDisk_TreatsAnUnreadableFileAsPopulated` | Ilegible ≠ vacío |
| `AppendRecordData_RoundTripsEveryField` | Los registros no spawneables sobreviven al guardado |
| `AppendRecordData_DoesNotInventOverrides` | Y no crecen overrides que nadie escribió |
| `PersistentLightCount_ExcludesDerivedLights` | La confusión que se saltó la guarda del Map Editor |
| `PersistentLightCount_IncludesUnspawnableRecords` | Y su reverso: la guarda no debe rechazar cada guardado en un mundo con un preset desconocido |

Suite completa EditMode: **5976 / 5976**. Consola limpia.

### Puntuación tras la Fase 0

| Área | Antes | Ahora | Por qué no es más |
|---|---|---|---|
| 1. Persistencia y seguridad de datos | 3.0 | **8.5** | Falta la ida y vuelta de 25 ciclos (punto 22) y el flag de sucio (19) |
| 2. Superficie funcional | 4.0 | 4.0 | Sin tocar — Fases 2 y 4 |
| 3. Paridad UI/UX | 4.5 | 5.0 | Solo la cabecera del panel de instancias dejó de mentir |
| 4. Robustez y tests | 4.0 | **6.5** | 10 tests nuevos, pero undo/redo (24) sigue sin cubrir y sigue roto |
| 5. Ciclo de vida, cableado e input | 6.0 | **7.5** | `_zoneManager` resuelto; queda restaurar estado en `Deactivate` (16) |
| **Global** | **4.1** | **6.3** | |

Lo siguiente es la Fase 1: las tres rutas de undo/redo que corrompen.

---

## Informe de la Fase 1 — completada (2026-08-26)

La fase 1 se planificó como "arreglar tres rutas de undo". Lo que se encontró es que las tres son
**un solo defecto**, y que arreglarlo destapa una capa entera de invariantes que nunca existieron.

Antes de tocar nada se lanzó un barrido de 57 agentes sobre el editor y su loader, con cuatro
lentes independientes (undo/redo, ciclo de vida, integridad de datos, alcanzabilidad) y un pase
adversarial que intentaba **refutar** cada hallazgo. **30 sobrevivieron, 21 murieron.** Un pase
final auditó el arreglo ya escrito y encontró cuatro huecos más en él.

### El defecto único

Los tres comandos capturaban un `GameObject`. Una referencia capturada muere con su objeto y no
se puede revivir, así que cada síntoma es la misma causa vista desde un ángulo distinto:

| Síntoma | Medido en Play Mode |
|---|---|
| El redo de un borrado era una lambda vacía — no había objeto vivo que volver a borrar | `10 → 9 → 10 → 10` |
| El redo de un spawn recreaba la luz pero no tenía dónde escribir la referencia nueva, así que el siguiente undo miraba un cadáver | `10 → 11 → 11 → 12` (huérfana, y el undo siguiente resucita otra) |
| El undo de un borrado reconstruía desde una clave de preset **parseada del nombre del GameObject** | `id=1 color=(0.10,0.90,0.30) intensity=2.75 radius=9.5` volvía como `id=15 color=none` |

Ese último salto a 12 es la prueba de que la pila y el mundo habían dejado de hablarse.

### El arreglo

Los comandos ya **no tocan un GameObject**. Direccionan por id estable y llevan un
`WorldLightLoader.LightSnapshot`, un objeto de valor sin referencias Unity:

```text
CaptureLight(GameObject) -> LightSnapshot    // id, preset, zona, rel, posicion, los 6 overrides
RestoreLight(LightSnapshot) -> GameObject    // mismo id, mismos overrides
FindLightById(int) -> GameObject             // resuelve en el momento de ejecutar
GetLightPresetKey(GameObject) -> string      // sustituye el parseo del nombre
```

Las mismas medidas, después:

```text
delete/undo/redo/undo:            10 -> 9 -> 10 -> 9 -> 10          OK
spawn/undo | redo/undo/redo/undo: 10 -> 11 -> 10 | 11 -> 10 -> 11 -> 10   sin huerfanas
BEFORE  id=1 color=RGBA(0.10, 0.90, 0.30) intensity=2.75 radius=9.5 zone=zone_100_50 rel=(1323,457)
AFTER   id=1 color=RGBA(0.10, 0.90, 0.30) intensity=2.75 radius=9.5 zone=zone_100_50 rel=(1323,457)
IDENTICAL: True
```

Y la clase entera cerrada: mover una luz, borrarla, deshacer el borrado (**objeto nuevo**),
deshacer el movimiento — la luz vuelve a su origen exacto, `(211.41, 96.09)`.

### Lo que el barrido añadió — pérdida de datos

1. **Un `falloff` autorado se leía, se ignoraba y se borraba.** La clave existe en el esquema y el
   lector la parseaba, pero `LightInstance` no tenía campo, `ApplyPresetToLight` no lo consultaba
   y el serializador de luces vivas no tenía rama para él. Autorarlo no cambiaba nada y el
   siguiente guardado lo eliminaba. Es la forma más silenciosa posible: el fichero sigue bien
   formado y la luz sigue iluminando. Ahora va de extremo a extremo.

2. **Un registro sin clave `zone` tiraba abajo el resto del fichero.** `JsonUtility` deja la
   cadena a `null`, `ZoneManager.TryGetZone` la pasa a `Dictionary.TryGetValue`, que **lanza**
   con clave nula — dentro del bucle de carga, sin guarda. Todos los registros posteriores no
   llegaban ni a la escena ni al conjunto preservado, y el siguiente guardado los borraba. La
   carga es ahora **por registro**: uno malo cuesta solo lo suyo.

3. **Una zona que este mundo no conoce no es una posición.** Caer al offset cero no da "más o
   menos bien", da otro sitio; el autor arrastra la luz para corregirla y `ResolveZoneAt` rebasa
   el registro a la zona donde cayó esa posición falsa. El registro queda corrupto por una
   edición que parecía una corrección. Ahora se preserva verbatim en lugar de colocarse. Una zona
   **vacía** sigue siendo legal — significa coordenadas absolutas.

4. **`ClearSpawnedLights` destruía luces que no puede recrear.** `BuildingObject` engancha su luz
   una sola vez, al aparecer el edificio, y `ReloadAllWorldContent` recarga los edificios primero
   y este loader después: cada farola del mundo se creaba y se destruía acto seguido, en cada
   cambio de slot y cada `reloadworld`, para el resto de la sesión. Medido antes: `derived=0`.
   Después: `derived=5`, y tras un `reloadworld` siguen siendo 5.

### Lo que el barrido añadió — corrupción de estado

1. **La pila de undo no se limpiaba nunca.** Ni al desactivar, ni al cambiar de slot, ni tras un
   `reloadworld`. Como los comandos direccionan por id, y los ids se reacuñan con cada mundo, un
   Ctrl+Z superviviente **no falla**: acierta, sobre otra luz. Ahora `Deactivate` la limpia y un
   contador `WorldGeneration` la descarta si el mundo se rehízo por debajo. Un historial *sin
   sembrar* no es un historial rancio — ese matiz costó un ciclo de medición.

2. **El pestillo del arrastre no recordaba QUÉ luz se pulsó.** Guardaba un booleano y un ancla, y
   elegía la luz al cruzar el umbral: pulsar sobre A y desplazarse hasta B arrastraba B con el
   ancla de A. El mismo pestillo sobrevivía a `Deactivate`, y `CancelMove` no lo soltaba — así que
   Esc no cancelaba el arrastre, se lo pasaba a otra luz. Ahora hay `_lmbPressedLight` y
   `ClearDragLatch()`.

3. **Ctrl+Z durante un arrastre era irrecuperable.** El arrastre reescribe la posición cada frame,
   así que el undo se aplicaba y se sobrescribía antes de verse, y el `Record` del `CommitMove`
   siguiente limpiaba la rama de redo. Ahora se rechaza con un mensaje.

4. **Las luces derivadas eran editables.** `DeleteLight`, `MoveLight` y `OverrideLight` las
   rechazan explicando por qué. Antes, arrastrar una desplazaba la llama de su farola de forma
   permanente, sin undo, y la barra de estado seguía diciendo "release LMB to drop".

### Lo que el pase adversarial encontró en el propio arreglo

1. **El invariante sobre el que descansa todo el diseño no existía: id positivo y único.**
   Una clave `"id"` ausente deserializa a **0**, que es justo el centinela de "no direccionable"
   que usan las luces derivadas — una luz autorada se colaba en esa clase y todos los comandos
   dirigidos a ella eran no-ops silenciosos. Dos registros con el mismo id resuelven al primero,
   así que borrar el segundo y pulsar redo destruye el primero. `NormaliseRecordIds` los repara al
   cargar y lo dice; `CaptureLight` rechaza un id inservible.

2. **Los ids se reciclaban.** `NextLightId` era `max(vivos)+1`. Borrar la luz de número más alto
   devolvía ese número al siguiente spawn — y el comando de borrado que seguía en el historial
   pasaba a nombrar la luz **nueva**, así que su redo la destruía. No hace falta ninguna
   referencia rancia: reciclar el id basta. El contador es ahora monótono dentro de una
   generación de mundo, y se resiembra del fichero cuando el mundo se reemplaza.

3. **`UndoStack` fallaba en silencio.** Los dos `catch` estaban vacíos: un undo roto era
   indistinguible de uno bueno mientras la pila seguía afirmando ediciones que el mundo nunca
   vio. Ahora se registra con la etiqueta del comando. El paso se consume igual, a propósito —
   cinco editores comparten esta clase y atascar el historial es peor para el autor que perder un
   paso. Y los comandos que *no lanzan* pero no encuentran su objetivo también avisan, que es el
   fallo que un `try/catch` no puede ver.

4. **`Destroy` no funciona fuera de Play Mode.** Registra "Destroy may not be called from edit
   mode!" y no hace nada, así que las rutas de destrucción del loader eran **intestables** desde
   EditMode: los fixtures escritos para demostrar que una recarga no se come las farolas no
   podían recargar nada. `DestroyLightObject` elige `DestroyImmediate` fuera de Play.

### Blindaje

| Fixture | Tests | Qué fija |
|---|---|---|
| `WorldLightLoaderPersistenceTests` | 12 | Fase 0 + id positivo en el fichero enviado |
| `LightingEditorUndoTests` | 9 | El snapshot no lleva objetos Unity, lookup por id sobrevive al reemplazo del GameObject, `WorldGeneration`, `UndoStack` no traga excepciones ni conserva la rama abandonada, y las firmas por id/snapshot |
| `WorldLightLoaderLoadSaveCycleTests` | 10 | Ciclos carga→edita→guarda completos contra repositorio y catálogo en memoria: `falloff`, registro que lanza, zona desconocida, zona vacía, teardown de derivadas, ids monótonos y su resiembra |

Un test estructural que probé primero — "ninguna lambda captura un objeto Unity" — resultó **falso
para este fichero** y se retiró: las filas del panel de instancias capturan legítimamente su propio
GameObject en un listener, y toda lambda que llame a un método de instancia captura `this`. Se
sustituyó por aserciones sobre las firmas, que expresan la misma intención sin falsos positivos.

Suite completa EditMode: **5997 / 5997**. Consola limpia. `light_instances.json` sin tocar en todo
el proceso — verificado contra git después de cada medición en Play Mode.

### Puntuación tras la Fase 1

| Área | Auditoría | Fase 0 | Ahora | Por qué no es más |
|---|---|---|---|---|
| 1. Persistencia y seguridad de datos | 3.0 | 8.5 | **9.5** | Falta el flag de sucio y el aviso al cerrar (punto 19) |
| 2. Superficie funcional | 4.0 | 4.0 | 4.0 | Sin tocar — Fases 2 y 4 |
| 3. Paridad UI/UX | 4.5 | 5.0 | **5.5** | Solo el arrastre y los mensajes; el solapamiento de paneles sigue |
| 4. Robustez y tests | 4.0 | 6.5 | **9.0** | 31 tests; falta la ida y vuelta de 25 ciclos (punto 22) |
| 5. Ciclo de vida, cableado e input | 6.0 | 7.5 | **8.5** | Queda restaurar los estados globales en `Deactivate` (punto 16) |
| **Global** | **4.1** | **6.3** | **7.5** | |

### Lo que el barrido dejó abierto (entra en las fases 2-4)

- Las luces puntuales están **apagadas** a la hora de inicio por defecto, así que el editor abre
  sobre un mundo invisible y su toggle necesita dos pulsaciones.
- El toggle "Amb / global" y el slider "Min intensity" **no hacen nada** con el `DayNightProfile`
  enviado, a ninguna hora.
- El panel de propiedades muestra el preset que el usuario eligió en la rejilla, **nunca la luz que
  clicó**, y jamás sus overrides por instancia.
- El editor **no suspende el combate**: cada clic de colocación también lanza el hechizo del
  jugador.
- Ambos caminos de reserva de `EnsureCatalog` apuntan a rutas que no existen.
- La lista de instancias se destruye y reconstruye entera dos veces por segundo, cambie algo o no.
- Un fichero ilegible bloquea todo guardado de la sesión y reporta un recuento inventado
  (`int.MaxValue`) — la guarda es correcta, el mensaje miente.

---

## Belleza de las luces — auditoría y primeras tres correcciones (2026-08-26)

Eje distinto al de las fases 0 y 1: aquellas trataban de que los datos no se perdieran; esta trata
de cómo se ve el resultado. Partió de una observación del autor — "no me parecen que sean hermosas
las luces como las tenemos actualmente" — y de una captura de la plaza a medianoche.

### Puntuación de partida: 3.3 / 10

| Dimensión | Peso | Nota | La cifra que la sostiene |
|---|---|---|---|
| Croma / color | 25% | 3.0 | Croma autorada 0.45, renderizada **0.159** |
| Interacción con la superficie | 20% | 3.5 | Contraste relativo del suelo 0.75 fuera, **0.15** dentro |
| Composición autorada | 20% | 2.0 | 9 de 10 luces del mismo preset; **0 de 11** fuegos con luz cerca |
| Forma y caída | 15% | 3.2 | Muere al 0.6 del radio autorado |
| Vida / movimiento | 10% | 2.6 | Solo se anima la intensidad; el Lamp oscila un 4% |
| Fundamento técnico | 10% | 7.4 | Contraste 7.9:1, dither calibrado, grade en LogC a 0.215 ms |

El barrido que produjo estas notas usaba 45 agentes con seis lentes y un pase adversarial; se cortó
a mitad por límite de sesión (11 completados). Los tres hallazgos marcados como **ruinosos** llegaron
sin verificar, así que se verificaron a mano. Los tres eran ciertos.

### 1. Faltaba la conversión gamma → lineal en el color de cada luz

El proyecto renderiza en espacio Lineal y cada textura se convierte al importarse, pero
`Light2D.color` es un campo C# que llega al shader tal cual: **URP no convierte nada en su ruta 2D**.
Un artista elegía (255, 200, 140) en la rueda de color y esos números sRGB se usaban como radiancia
lineal. La codificación de vuelta a pantalla acerca todas las proporciones a 1 —
`0.784^(1/2.2) = 0.895`, `0.549^(1/2.2) = 0.766` — así que una saturación autorada de 0.45 llegaba
como 0.16.

Verificado con un A/B sobre el mismo frame, misma luz, medianoche:

```text
ANTES  (sRGB usado como radiancia)   sat=0.167   lum=0.507
AHORA  (.linear)                     sat=0.351   lum=0.445
```

Croma **+110%**. El brillo baja un 12% y eso es correcto: un color saturado es menos luminoso que
uno lavado. El canal de pico no se toca — `linear(1.0) == 1.0` — así que ninguna luz pierde su techo.

Junto a esto, los diez registros del fichero fijaban su color con un override **bit a bit idéntico
a su propio preset**. Mientras se respetaban, retocar `LightPreset_Torch.color` no cambiaba nada y
nada lo explicaba. Un override igual a su preset se ignora ahora al cargar, con una línea en
`VerboseLog`; no cuesta ningún píxel hoy, porque los valores son iguales por construcción, y
devuelve a los presets su trabajo.

### 2. El fuego y la luz eran dos colocaciones independientes

El mundo enviaba **11 emisores `torch_flame`** y **9 luces Torch**, colocados por separado. Medidas
las 11 distancias a la luz más cercana de su misma zona:

```text
0 de 11 llamas tienen una luz dentro del alcance real de una antorcha (2.4 u)
2 de 11 dentro del radio autorado (4.0 u)
distancia minima en todo el mundo: 2.47 u; la mayoria entre 4 y 12.6
```

Es decir: **lo que ardía no iluminaba, y lo que iluminaba no tenía nada ardiendo dentro.** Es la
razón más directa de que la captura no se leyera como una plaza de noche.

La corrección es simétrica a algo que ya funcionaba: un edificio-farola lleva su luz mediante
`BuildingTemplateData.lightPresetKey` + `RegisterDerivedLight`. Ahora un preset de partículas puede
hacer lo mismo — `ParticlePresetDefinition.lightPresetKey` y `lightHeightOffset` —, y
`ParticleInstancesLoader` engancha las luces en cuanto el loader existe, con la misma espera de 300
frames que usa `BuildingObject`. Las luces son **derivadas** (`persistent = false`), así que nunca
llegan a `light_instances.json` y no pueden duplicarse al guardar: el registro del emisor ya es la
colocación autoritativa.

`PP_torch_flame` quedó con `lightPresetKey = "Torch"` y `lightHeightOffset = 0.35` — la llama arde
por encima de su ancla, y su luz también. Medido: `derived` pasó de **5 a 16**, las 11 llamas, cada
una con su luz a 0.35 u.

### 3. La luz aditiva no ilumina: pinta encima

Confirmado en la fuente de URP, `CombinedShapeLightShared.hlsl`:

```hlsl
finalOutput = _HDREmulationScale * (color * finalModulate + finalAdditve);
```

El término aditivo **no** se multiplica por el albedo. Una luz aditiva deposita su color sobre el
frame ignorando lo que hay debajo, y por eso el adoquín desaparecía dentro del charco.

`Light2DBlendStyle.blendFactors` está cableado a 1/0 o 0/1 según el modo — la estructura
`BlendFactors` que hay al lado no se usa —, así que un solo blend style no puede hacer las dos
cosas. Una luz que ilumine Y brille tiene que ser dos `Light2D`: un **cuerpo** en el buffer Multiply
que comparte con la ambiental (los dos se acumulan ahí, de modo que la superficie queda escalada por
su suma y conserva color y textura) y un **núcleo** aditivo pequeño para la parte que debe leerse
como demasiado brillante para mirarla.

Tres campos nuevos en `LightPresetDefinition`: `surfaceMix` (0 = comportamiento anterior),
`coreScale` y `surfaceGain`. El parpadeo escala ambas mitades desde intensidades cacheadas — animar
solo una haría que el charco cambiara de **color** al parpadear, porque las dos llegan al frame por
términos distintos del compuesto.

Barrido medido sobre `Light_3_Torch`, medianoche, ortho 5, parche 31x31 en el centro del charco:

| mix | gain | lum | sat | textura |
|---|---|---|---|---|
| 0.00 (aditivo puro) | - | 0.512 | 0.357 | **0.017** |
| 0.60 | 5.0 | 0.368 | 0.451 | 0.128 |
| 0.75 | 3.0 | 0.313 | 0.464 | 0.138 |
| **0.75** | **5.0** | **0.347** | **0.481** | **0.151** |
| suelo sin luz | - | 0.147 | 0.520 | 0.112 |

`surfaceMix 0.75`, `surfaceGain 5.0` en Torch, Candle y Lamp; **0.55** en Magic, porque una luz
arcana debe leerse como antinatural y conserva más velo a propósito.

### Resultado medido, contra la línea de partida

| | luminancia | saturación | textura del suelo |
|---|---|---|---|
| Antes de todo | 0.357 | 0.202 | (aditivo puro: 0.017) |
| Ahora | 0.361 | **0.480** | **0.150** |

Brillo intacto, croma **+138%**, textura del adoquín **×8.8**. El charco sigue a 2.4 veces la
luminancia del suelo que lo rodea, así que sigue siendo un charco.

### Un test hizo exactamente su trabajo

`ParticleEmitterSortingAndAmbientTests.ApplyPreset_AShippedAssetThatOmitsTheSortingKeys_...` falló
al guardar `PP_torch_flame` por Unity: re-serializar un asset rellena **todos** los campos ausentes
con su valor por defecto, así que aparecieron las cuatro claves de ordenación — todas con el valor
que la ruta de clave-ausente ya producía, de modo que nada cambió en ejecución, pero el fichero dejó
de ejercitar el camino que ese fixture existe para cubrir. El test lo dijo en vez de pasar por la
razón equivocada. Se repuntó a `PP_chimney_smoke`, uno de los 118 presets que aún omiten las cuatro.

### Lo que sigue abierto

- Las cookies de luz: todas siguen siendo el mismo círculo pelado, sin forma que diga qué fixture
  la produjo. Es el techo de cuánto puede mejorar esto.
- La paleta: cuatro presets, tres de ellos dentro de 14 grados de tono.
- Los 20 sitios que escriben `Enum.ToObject(type, 2)` — que en URP 14 es `Sprite`, no `Point`. Bola
  de fuego, slash, dash, mina, tótem y aura no proyectan un solo fotón. Dos de esos sitios llevan el
  comentario `// 2 = Point`.
- El parpadeo entrega la mitad de su amplitud autorada porque el remapeo de Perlin supone un rango
  [0,1] que el ruido no alcanza.
- Las luces puntuales están apagadas a la hora de inicio por defecto: el editor abre sobre un mundo
  invisible.
