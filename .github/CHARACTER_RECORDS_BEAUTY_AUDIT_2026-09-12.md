# Auditoría de belleza de las pestañas CHARACTER y RECORDS

**Fecha:** 2026-09-12 · **CHARACTER: 2.1 / 10** · **RECORDS: 1.4 / 10** · Objetivo: **≥ 8.5**

Alcance: las otras dos pestañas de la hoja de personaje —
`Gameplay/HUD/CharacterStatsHUD.cs` (230 líneas) y `Gameplay/HUD/StatisticsHUD.cs` (204)— más
la capa de datos que RECORDS dibuja (`ProfileTelemetrySystem`, `IProfileDb`,
`GameEvents.FireRunEnded`) allí donde el panel dice algo que no es cierto.

Completa la serie de la hoja: [SKILLS](SKILLS_TAB_BEAUTY_AUDIT_2026-09-12.md) (reconstruida el
mismo día, 1.8 → 8.x) y [GRIMOIRE](GRIMOIRE_BEAUTY_AUDIT_2026-09-12.md). Las cuatro comparten
canvas, rect, fuente y defectos; las dos primeras ya están hechas y esta cierra el juego.

Método: lectura del código, y medida en vivo a 1600x800 por `execute_code` sobre una sesión de
Play que otra sesión tenía abierta — **todo de solo lectura**, sin abrir ni cerrar nada, usando
los seams puros (`ComputeSheetText`, `ComputeStatsText`) y el `cachedTextGenerator` del propio
label. Cada nota va con su número.

---

## 1. Resumen

**CHARACTER es una tabla que no está alineada, y no puede estarlo.** El código maqueta columnas
con `PadRight(20)` y `PadLeft(8)` —aritmética de fuente MONOESPACIADA— y luego las dibuja con
`LegacyRuntime.ttf`, que es Arial y es proporcional. Medido sobre las catorce filas de
estadística: la columna de valor arranca en catorce x distintas con una **dispersión de 87 px**.
No es que esté mal alineada; es que el mecanismo de alineación no existe. Y el bloque entero
ocupa **316 px de los 883** que tiene el cuerpo: el 64 % del panel está vacío mientras la tabla
se apila en una columna estrecha a la izquierda.

**RECORDS no tiene un problema de belleza: dice cosas que no son verdad.** Imprime
`Total runs: 0` encima de una lista de **251 partidas**. Dice que el tiempo jugado total es `—`
y que la duración media es `—`, y en la base **251 de 251 partidas tienen duración 0**. Dice
`kills=0` en cada fila mientras su propia tabla de muertes suma 39. Y su top de monstruos
incluye **tres filas de vendedores** (`vendor_blacksmith_smith`, `vendor_lumberjack_pavel`), con
la clave de base de datos en crudo como nombre.

La causa de casi todo ello es una sola línea que no existe: **`GameEvents.FireRunEnded()` no
tiene ni un llamador en producción.** Lo llama un test y nadie más. El juego ABRE una partida en
cada arranque (`StartRun` sí se llama, desde la secuencia de boot) y no CIERRA ninguna nunca.

---

## 2. Puntuación por eje

### CHARACTER — 2.1 / 10

| # | Eje | Nota | Evidencia medida |
| --- | --- | --- | --- |
| 1 | Alineación de columnas | 0 | `PadRight(20)`/`PadLeft(8)` sobre `LegacyRuntime` (Arial, proporcional). La columna de valor arranca en x de **-423 a -336: 87 px de dispersión** en catorce filas. El mecanismo no funciona; no es cuestión de ajustar el padding |
| 2 | Aprovechamiento | 1 | El texto mide **316x336 px en un cuerpo de 883x487**: el 64 % del ancho vacío. Una tabla de 14 filas x 8 columnas de desglose apilada en una columna estrecha |
| 3 | Tipografía | 0.5 | `UnityEngine.UI.Text` + `LegacyRuntime.ttf`. Tercera familia del proyecto, la misma que SKILLS acaba de retirar |
| 4 | Jerarquía | 1 | Un solo `Text`. Cabecera, nombres, valores y desglose son el mismo tamaño y el mismo color. Nada distingue un total de un sumando |
| 5 | Materialidad de la placa | 1 | `Image` sin sprite a `(0,0,0,0.85)` — el mismo filtro que SKILLS midió en ×3.04 de dispersión de luminancia |
| 6 | Idioma | 1 | `Character`, `Stats`, `Level`, `Skill points`, `Arcane points`, `base`, `level`, `equipment` en inglés, y en la misma línea `Vida máxima` y `Daño cuerpo a cuerpo` en español porque `StatCatalog` sí está traducido. Viola R15 |
| 7 | Caracteres de dibujo | 2 | `──── Character ────` usa U+2500. Sobrevive en Arial y no sobrevive en la cara de píxel del HUD, que es a donde tiene que ir |
| 8 | Semántica del color | 1 | Dos colores en todo el panel: blanco de cabecera y `(0.90,0.92,0.96)` de cuerpo. Ni vida en verde, ni maná en azul, ni oro para el nivel. R6 no aparece |
| 9 | Lectura del desglose | 3 | El desglose es el mejor acierto del panel —`14 = 2 base + 4 level + 6 equipment`— y se pierde en el mismo gris que todo lo demás. Una capa que ha dejado de aportar se ve solo si la buscas |
| 10 | Partículas / eventos | 0 | Ninguna. Subir de nivel, equipar un arma o comprar un talento cambian números aquí y no producen un píxel |
| 11 | Movimiento | 0 | `SetActive`. Sin fundido |
| 12 | Iconografía | 0 | Catorce estadísticas, cero iconos. `HudArt` ya trae corazón, gota y candado |
| 13 | Desbordamiento | 6 | Hoy NO se trunca (756/756 caracteres, 21 líneas en 487 px) porque otra sesión arregló el canvas a factor 1.0. Sigue sin `ScrollRect`: es holgura, no diseño |
| 14 | Accesibilidad | 2 | Todo por posición y texto, en inglés, sin escala, sin redundancia de forma |

### RECORDS — 1.4 / 10

| # | Eje | Nota | Evidencia medida |
| --- | --- | --- | --- |
| 1 | Verdad de los totales | 0 | `Total runs: 0` con **`runs.GetAll().Count = 251`**. El panel se contradice a sí mismo en la misma pantalla |
| 2 | Verdad de los tiempos | 0 | `total_playtime_sec = 0` y `AverageDurationSeconds = 0`, con **251 de 251 partidas a duración ≤ 0**. Ninguna partida ha registrado nunca cuánto duró |
| 3 | Verdad de las muertes por partida | 0 | **249 de 251 partidas con `kills=0`**, mientras la tabla de muertes suma 39 en 7 filas. Solo persisten las dos partidas en que el jugador murió, porque `Runs.Update` únicamente se llama desde `OnPlayerDied` y desde `OnRunEnded` |
| 4 | La causa raíz | 0 | **`GameEvents.FireRunEnded()` tiene cero llamadores en producción** (grep: solo `GameEvents.cs` que lo declara y un test). `StartRun` sí se llama desde el boot. El juego abre una partida por arranque y no cierra ninguna |
| 5 | Nombres mostrados al jugador | 0 | El top imprime `barbol`, `barbol_oscuro`, `vendor_blacksmith_smith`: claves de base de datos, no nombres. Es el mismo defecto que la auditoría de misiones encontró con `vendor_banker_abigail` |
| 6 | Qué cuenta como monstruo | 0.5 | **3 de las 7 filas del top son VENDEDORES.** "Top monster kills: el herrero" |
| 7 | Logros | 1 | `Achievements: 0` con `UnlockedCount() = 0`. Ningún logro se desbloquea nunca en producción |
| 8 | Profundidad | 1 | `depthReached` es, por comentario del propio código, "un proxy del nivel máximo". Imprime `depth=1` en todas las filas y se llama "profundidad" |
| 9 | Nomenclatura | 2 | La pestaña se llama **RECORDS** y la cabecera del panel dice **Statistics**. Dos nombres para una pantalla |
| 10 | Idioma | 1 | Todo en inglés: `Lifetime`, `Total runs`, `Top monster kills`, `Recent runs`, `alive`, `(no kills yet)` |
| 11 | Tipografía, placa, jerarquía, color, partículas, movimiento, iconos | 0.5–1 | Idénticos a CHARACTER: un `Text`, `LegacyRuntime`, placa a 0.88, dos colores, cero eventos |
| 12 | Corte silencioso | 1 | Imprime como mucho 10 partidas de 251 y no dice que hay más. No hay paginación, ni scroll, ni "y 241 más" |

---

## 3. Defectos en orden de gravedad

1. **Nada cierra una partida** (RECORDS 4). Una línea que no existe explica los totales a cero,
   los tiempos a cero y las duraciones a cero. Es un defecto de GAMEPLAY que solo se ve desde
   esta pantalla — exactamente el argumento que el comentario de cabecera de `CharacterStatsHUD`
   hace sobre sí mismo: "un número sin pantalla es un número que nadie puede notar que está
   roto".
2. **El panel se contradice** (RECORDS 1). `Total runs: 0` sobre 251 filas. Aunque se arregle el
   contador, un panel que suma dos veces la misma cosa por dos caminos distintos volverá a
   discrepar: el total tiene que DERIVARSE de la tabla, no contarse aparte.
3. **Los vendedores están en el top de monstruos** (RECORDS 6) y se muestran por su clave de
   base de datos (RECORDS 5).
4. **Las columnas de CHARACTER no pueden alinearse** (CHARACTER 1). 87 px de dispersión, y el
   arreglo no es más padding: es dejar de maquetar con espacios.
5. **El 64 % del panel está vacío** (CHARACTER 2) mientras la tabla se estrecha.
6. **Las dos placas son filtros** (CHARACTER 5, RECORDS 11), con el número ya medido en SKILLS.
7. **Las dos pestañas en inglés** dentro de una hoja cuyas otras dos ya están en español.
8. **Cero eventos y cero iconos** en las dos.

---

## 4. Qué deberían ser

**CHARACTER: una ficha con dos columnas y el desglose como ciudadano de primera.**

La información ya está y es buena — catorce estadísticas, siete capas, `GetLayerContribution`
por capa. Lo que falta es dejar de dibujarla como un `printf`:

- **Columna izquierda: el personaje.** Retrato horneado (`HudTextureBaker.Portrait` ya existe y
  lo usa el panel del jugador), nivel en el medallón, barra de experiencia, y las dos monedas
  —puntos de habilidad y puntos arcanos— con la misma insignia que la barra de acciones.
- **Columna derecha: la tabla.** Una fila por estadística: icono, nombre, valor grande a la
  derecha, y **el desglose como una barra apilada de siete segmentos**, uno por capa, cada uno
  del color de su capa. Eso convierte "14 = 2 base + 4 level + 6 equipment" en algo que se lee
  de un vistazo y hace que una capa que ha dejado de aportar sea un segmento que falta.
- **La fila se expande al pasar por encima** para dar los números del desglose en texto. La
  barra responde "de dónde viene"; el texto responde "cuánto exactamente", y solo una de las dos
  preguntas se hace todo el rato.
- **Un evento y solo uno**: cuando una estadística CAMBIA mientras la pestaña está abierta, su
  fila hace el mismo golpe de un texel y el mismo puñado de motas del color del stat que el
  tablero de talentos ya usa. Comprar un talento con la ficha abierta tiene que verse.

**RECORDS: primero que sea verdad, y después que sea bonito.**

Ninguna cantidad de piedra y oro arregla `Total runs: 0`. El orden es:

1. **Cerrar la partida.** `GameEvents.FireRunEnded()` desde donde una partida acaba de verdad:
   volver al menú, muerte con permadeath, salir del juego. Y `Runs.Update` en el autosave, no
   solo al morir, o una partida sin muerte sigue sin registrar sus muertes.
2. **Derivar los totales de la tabla**, no de un contador paralelo. `total_runs` y
   `total_playtime_sec` son sumas de `Runs.GetAll()`; mantenerlos aparte es la forma de que dos
   números sobre la misma cosa discrepen, que es lo que ya ha pasado.
3. **Filtrar la facción** en el top de muertes: `EntityFaction.AuthoredFaction` decide si algo
   cuenta como monstruo, exactamente igual que hace la puerta del botín y de las monedas. Matar
   al herrero no es una gesta.
4. **Resolver el nombre**: `MonsterCatalog` tiene el `displayName`; la clave es para el disco.
5. Y entonces: tarjetas de "vida" (partidas, tiempo, muertes, logros) arriba, el top como
   filas con el icono del monstruo, y la historia de partidas como una lista con scroll que
   dice cuántas hay.

**Las dos van al mismo sitio que SKILLS**: `Valkur.UI` (que puede ver `Valkur.Gameplay`, y no al
revés), espacio de texel, `HudArt`, `HudPixelFont`, `HudTheme`, `HudMoteLayer`, y su propio
`Data/UI/*Style.cs` bajo `Resources/UI/`.

---

## 5. Lo que la serie de la hoja deja demostrado

Cuatro pestañas, cuatro auditorías, y el mismo patrón en las cuatro: **cada mitad correcta y el
producto mal.**

- SKILLS: `row`/`column` autorados y usados como clave de ordenación.
- GRIMOIRE: el mismo cromo con otra escala.
- CHARACTER: aritmética de monoespaciado dibujada con una proporcional.
- RECORDS: un sistema de telemetría completo, probado, y un evento que nadie dispara.

Y en las cuatro, el defecto sobrevivió porque **ninguna sonda estructural puede verlo**. Los
251 runs están ahí, el `Text` tiene su texto, el canvas tiene su orden. Lo único que separa
"funciona" de "es verdad" es mirar la pantalla y comparar dos números que deberían coincidir.

---

## 6. Orden propuesto

### Fase 0 — Verdad (RECORDS 1.4 → 4.0)

1. `FireRunEnded` desde los tres sitios donde una partida acaba.
2. `Runs.Update` en el autosave.
3. Totales derivados de la tabla.
4. Facción y `displayName` en el top de muertes.
5. Un test sobre los datos ENVIADOS —el perfil de esta máquina— que falle si
   `total_runs` discrepa de `Runs.Count`, como `ShippedSkillTreeDataTests` hace con los árboles.

### Fase 1 — El dialecto (las dos → ~6.5)

6. `git mv` a `UI/HUD/Sheet/`, namespace `Valkur.UI.HUD`.
7. `CharacterHudStyle` + `RecordsHudStyle`, leyendo `HudTheme`.
8. Espacio de texel, `HudArt`, `HudPixelFont`, piedra opaca y velo.
9. `CharacterText` y `RecordsText` por `GameLanguage.Pick`.

### Fase 2 — La forma (→ ~8.0)

10. CHARACTER: retrato, medallón, barra de XP, y la tabla con barra apilada por capa.
11. RECORDS: tarjetas de vida, top con iconos, historial con scroll y su cuenta real.

### Fase 3 — Feel (→ ≥ 8.5)

12. Fundido, golpe de un texel y motas al cambiar una estadística.
13. Insignia de puntos sin gastar en la pestaña, como el verbo Talentos ya tiene.

---

## 7. Qué NO hacer

- **No maquetar columnas con espacios.** Ni con `PadRight`, ni con tabuladores, ni con una
  fuente "casi monoespaciada". Una columna es un rect.
- **No arreglar `Total runs: 0` subiendo el contador.** Mientras haya dos caminos al mismo
  número, volverán a discrepar. Que lo derive la tabla.
- **No filtrar los vendedores por nombre** (`StartsWith("vendor_")`). La facción autorada ya
  responde a esa pregunta y es la que usan el botín y las monedas.
- **No mostrar una clave de base de datos a un jugador**, en ninguna pantalla.
- **No meter `ScrollRect` en CHARACTER como arreglo del aprovechamiento.** Cabe de sobra; lo que
  sobra es el hueco, no el contenido.

---

## 8. Resultado de la reconstrucción (mismo día)

### Fase 0 — la verdad, verificada en vivo

El defecto no era de dibujo. `GameEvents.FireRunEnded()` no tenia ni un llamador en produccion;
`StartRun` si se llama desde el boot. Medido en el `profile.json` de esta maquina antes y despues
de arrancar y cerrar UNA partida:

```text
antes    runs 251   withDuration 0   total_runs: <la clave no existia>
despues  runs 252   withDuration 1   total_runs 1   total_playtime_sec 6.854
         ultima: ordinal=252  duration=6.85  ended=2026-09-12T12:26:20
```

El cierre va por CONDICIONES y no por llamadas: el sistema que posee la partida la cierra cuando
su escena se va, cuando la aplicacion termina, y cuando permadeath la acaba por definicion. Una
forma nueva de salir del mundo no puede olvidarse de cerrarla. Mas `Runs.Update` en cada muerte
—antes solo se persistia al morir el jugador, y por eso 249 de 251 filas decian `kills=0`— y el
filtro de faccion AUTORADA en el contador de bajas, que es el mismo criterio que ya usan las
puertas del botin y de las monedas.

El total, ademas, se DERIVA de la tabla. El contador paralelo ya funciona y sigue sin ser la
fuente: solo puede describir partidas cerradas desde hoy, o sea 1 contra 252 filas.

### Fase 1 — las dos pestañas

| Fichero | Que es |
| --- | --- |
| `UI/HUD/Sheet/SheetPanelChrome.cs` | El cromo compartido: canvas, velo, piedra, cabecera, pie, contra-escala y fundido |
| `UI/HUD/Sheet/CharacterSheetHUD.cs` | Identidad a la izquierda, tabla a la derecha, una mota por estadistica que cambia |
| `UI/HUD/Sheet/CharacterStatRow.cs` | Una fila: nombre, valor, y el desglose por capa como barra apilada |
| `UI/HUD/Sheet/RecordsHUD.cs` | Tarjetas de vida, top de bajas y el historial, todo derivado de la tabla |
| `Gameplay/HUD/SheetText.cs` | Las cadenas de las dos, por `GameLanguage.Pick` |
| `Data/UI/SheetHudStyle.cs` | Un asset para las dos: son dos vistas de la misma ventana |
| `Gameplay/Player/SkillLock.cs`-style: `EntityFaction.AuthoredSideOf` | La faccion autorada, separada de la derivada |

Tests: `RunLifecycleTests` (5), `SheetPanelContractTests` (9), mas los 11 de
`ProfileTelemetrySystemTests` que ya existian. **25 de 25 en verde.**

### Los cuatro defectos que solo la captura vio

Con las columnas alineadas, los estados correctos, la banda de orden correcta y la consola
limpia, la ventana seguia estando mal en cuatro sitios:

1. **La cara `Large` no tiene letras.** `SheetText.Duration(58)` devuelve `"58s"` y la tarjeta de
   tiempo jugado mostraba `58`: `HudPixelText` salta en silencio un caracter que su cara no sabe
   deletrear. Tercera aparicion del mismo defecto en dos dias — la primera borro el titulo del
   tablero de talentos.
2. **Las partidas "recientes" eran las mas antiguas.** `GetAll()` ya devuelve newest-first
   —medido: `first=2026-09-12`, `last=2026-05-08`— y el panel la recorria al reves.
3. **El segmento `Base` era invisible**, pintado `stoneLight` sobre piedra oscura. Base es la
   capa que TODA estadistica tiene, asi que casi todas las barras parecian empezar por la mitad
   de su propia pista.
4. **La columna de identidad estaba vacia**: un hueco alto sin nada. Lleva el retrato del panel
   del jugador, reutilizado entero en vez de resuelto otra vez.

### Dos tests que suspendian codigo correcto

Los dos son limitaciones de EditMode y merecen quedar escritas:

- **Ni `OnEnable` ni `OnDestroy` se entregan** a un componente añadido con `AddComponent` en
  EditMode, asi que las suscripciones no ocurren y el teardown tampoco. La fixture que ya existia
  lo sabia e invocaba `OnEnable` por reflexion; la nueva no, y tres tests fallaron sobre codigo
  bueno.
- **`Time.time` no avanza** entre dos llamadas, asi que afirmar que una duracion medida es
  mayor que cero suspende una implementacion correcta. Lo que se puede afirmar ahi es que la
  partida quedo CERRADA y SELLADA; que lo este con una duracion real se midio en Play Mode.

### Y una comprobacion que el 1600x800 no puede hacer

`TheWindow_FitsEveryResolutionTheGridSupports` recorre 1280x720, 1600x800, 1920x1080, 2560x1440
y 3840x2160 con la escala que `HudPixelScaleFor` da en cada una. Estas dos ventanas declaran
texels FIJOS, asi que deberian ser inmunes al defecto que rompia el grimorio en pantallas BUENAS
—un panel que derivaba sus texels dividiendo un lienzo fijo entre la escala tenia menos texels
cuanto mas DPI— pero "deberian" es exactamente lo que ha fallado cinco veces en esta serie.

### Recaptura: los cuatro arreglos, confirmados en pantalla

| | antes | después |
| --- | --- | --- |
| Tarjeta de tiempo jugado | `58` | `9M 48S` |
| "Partidas recientes" | las más antiguas (mayo) | 2026-09-12, con duraciones reales |
| Segmento `Base` | invisible | barra completa en las 14 filas |
| Columna de identidad | vacía | retrato |

Y la telemetría quedó probada de punta a punta sin buscarlo: el pie de RECORDS dice
**`HISTÓRICO 10 / 262`**. Diez partidas cerradas con duración donde por la mañana había 251
filas y CERO. Cada entrada y salida de Play de la tarde fue cerrando la suya, que es la prueba
de que el cierre por CONDICIONES funciona sin que nadie llame a nada.

Tests finales: **71 de 71** en las siete fixtures (`summary.total: 71`, 4,79 s), tras un gate
con las cuatro puertas — `isPlaying`, `isCompiling`, `canReloadAssemblies` (que es `internal` y
hay que alcanzar por reflexión), `unityRuns` — más frescura de DLL POR ENSAMBLADO y la pregunta
que la frescura no contesta: si el símbolo concreto está dentro.

### Lo que queda abierto

1. Retirar `CharacterStatsHUD` y `StatisticsHUD`: ya no los llama nadie, pero una fixture de otra
   sesión los nombra y se acordó que repunte primero.
2. `depthReached` sigue siendo, por comentario del propio código, un proxy del nivel máximo, y la
   tarjeta lo llama "nivel alcanzado". O se mide la profundidad de verdad o se le cambia el nombre.
3. Ningún logro se desbloquea nunca en producción: `UnlockedCount()` es 0 y la tarjeta lo dice.
   Es el siguiente "autorizado e inerte" de esta zona.
