# Valkur — auditoría global del juego

> Auditoría completa del proyecto, eje por eje, puntuada 0–10. Fecha: **2026-09-08**.
> Medida sobre el árbol de trabajo (`main`, commit `5093a9940`), contando datos reales
> enviados y no intenciones. Cada nota lleva la evidencia que la sostiene.

## Veredicto en una línea

**El motor es excelente y el juego no existe todavía.** Valkur tiene la infraestructura
de un producto terminado — arranque instrumentado, guardado atómico, 17 editores de
autoría, 7 664 tests, entrada centralizada, IA con percepción y amenaza — montada
alrededor de un mundo en el que **no hay nada que hacer**: sin objetivo, sin misiones,
sin estructura de partida, sin final. La distancia entre las dos mitades es la
puntuación.

**Nota global ponderada: 4.4 / 10.**

## Escala

| Nota | Significado |
|---|---|
| 0–1 | No existe, o existe como código muerto que nada ejecuta |
| 2–3 | Existe el andamiaje; ningún jugador lo notaría en una partida |
| 4–5 | Funciona, pero le falta la mitad o el contenido que lo haría importar |
| 6–7 | Sólido y jugable; con huecos identificados |
| 8–9 | Terminado y verificado, con las decisiones documentadas |
| 10 | No se lo doy a nada. Siempre queda algo |

## Resumen por bloques

| Bloque | Peso | Media | Aportación |
|---|---|---|---|
| 1. El juego como juego | 30 % | **1.4** | 0.43 |
| 2. Combate | 20 % | **6.1** | 1.21 |
| 3. Progresión y economía | 15 % | **5.7** | 0.86 |
| 4. Mundo y contenido | 15 % | **4.4** | 0.65 |
| 5. Presentación | 10 % | **6.4** | 0.64 |
| 6. Producción e ingeniería | 10 % | **5.8** | 0.58 |
| | | | **4.4 / 10** |

El bloque 1 pesa el 30 % a propósito: es el único que responde a *por qué alguien
jugaría*. Es también el más bajo con diferencia, y por eso ningún avance en los otros
cinco mueve la nota global de forma apreciable. Subir el bloque 1 de 1.4 a 6.0 —
sin tocar nada más — llevaría el total a **5.8**.

---

# Bloque 1 — El juego como juego (peso 30 %) — 1.4

## 1.1 Estructura de partida / bucle roguelike — **2.0**

Existe el vocabulario de una run (`run_id`, `run_ordinal` en los metadatos del guardado,
`ProfileTelemetrySystem`, `PermadeathSaveCleanupSystem`) y no existe la run. El mundo es
un mapa abierto de 26 zonas de 50×50 que se carga entero y siempre igual. No hay
progresión de contenido, ni pisos, ni sellos, ni un "más profundo".

Evidencia: `permadeath = false` por defecto en `GameSettings.cs:21`; 26 ficheros en
`StreamingAssets/Maps/`; la generación BSP existe pero produce geometría sin poblar
(ver 4.3).

## 1.2 Objetivo, victoria y final — **0.5**

Cero coincidencias en todo `Scripts/` para `victory`, `GameOver`, `endgame` o
equivalente. El jugador nace, puede subir hasta nivel 60 (`XpCurve.asset: levelCap: 60`)
y no hay ningún estado del juego que declare que ha terminado. No es que el final esté
sin escribir: **no hay sitio donde escribirlo**.

## 1.3 Curva de dificultad y ritmo — **2.5**

Las piezas están: `EncounterDifficulty` (bonus de LUGAR + escala de PROGRESO),
`SpawnLevel` sellado en el objeto, `levelHpGrowth` proporcional en los 18 hostiles.
Lo que falta es la curva: no hay modos de dificultad, ni regiones ordenadas por nivel,
ni una razón para que el jugador esté en la zona A antes que en la B. 17 spawners
colocados en todo el mundo, de los cuales 6 son respawns de vendedores.

## 1.4 Onboarding / tutorial — **0.5**

`Tutorial` aparece más de diez veces en el código y **todas** dentro de los editores de
autoría (`BuildingsRuntimeEditor.Chrome`, `CameraRuntimeEditor`, …): son los tutoriales
para el *autor*, no para el jugador. Un jugador nuevo arranca en un mundo abierto sin una
sola línea que le diga qué tecla hace qué — agravado por que los catorce toggles de editor
se retiraron y las pistas de la pantalla de carga que los mencionaban se borraron sin
sustituto.

## 1.5 Misiones / quests — **1.0**

`QuestDefinition`, `QuestManager`, `IObjective` y `KillCountObjective` están escritos y
probados. Y:

- **Cero misiones enviadas.** Ni un `.asset` de `QuestDefinition` en todo el proyecto.
- **`QuestLogHUD` no lo instancia nadie.** Grep de `QuestLogHUD` fuera de su propio
  fichero: 0 resultados. El HUD construye su raíz en `OnEnable`, y nada lo enciende.
- **`StartQuest` no tiene ningún llamante de producción** — solo la recarga interna del
  propio manager (`QuestManager.cs:166`).
- **No hay dador de misión.** `NPCInteractable.Interact()` abre la tienda; no hay
  componente que ofrezca una misión, ni entrada en la ficha de personaje.
- `QuestDefinition` no tiene campo de oro.

Es la pieza de mayor apalancamiento del proyecto entero: convierte los NPCs, la economía,
el mundo y el botín en un bucle. Está a un 80 % de código y a un 0 % de datos.

> **Actualizado el mismo día: 1.0 → 6.0.** Diez misiones enviadas, nueve tipos de
> objetivo implementados, dador y entrega dentro de la conversación, registro en
> pantalla, persistencia y consola. Sigue sin haber misiones repetibles, ramas,
> marcadores en el minimapa ni escoltas. Detalle: `.github/QUESTS_TEN_DND.md`.

## 1.6 Endgame y rejugabilidad — **2.0**

`IProfileDb` guarda historial de runs, estadísticas de muerte y logros, y `StatisticsHUD`
los pinta. Pero sin bucle de partida no hay nada que registrar salvo tiempo jugado, no
hay desbloqueos entre runs, no hay semillas compartibles (la única semilla determinista
del proyecto es la del mercado), y el permadeath está apagado por defecto.

---

# Bloque 2 — Combate (peso 20 %) — 6.1

## 2.1 Combate cuerpo a cuerpo — **7.0**

Sólido y con buen tacto: `PollCombatActions` es el único lector de la superficie de
guerra, `SlashProfile` deriva la silueta del arco, el daño barre con el filo dibujado,
hay hit-stop, `ComboCounter`, `MeleeReachScale` por clase y postura Paz/Guerra. Lo que
baja la nota: **el jugador nunca pasa por `MeleeCombat`** (solo los monstruos), así que
media rama de estadísticas llegó tarde a tener efecto, y no hay bloqueo, parada ni
esquiva para el jugador — los enemigos oscuros sí esquivan (`DodgeState`) y el jugador
no tiene respuesta equivalente más allá del dash.

## 2.2 Sistema de hechizos — **8.0**

Lo mejor del proyecto. 26 ejecutores, 104 assets (82 hechizos reales + 22 sondas de
animación), anclas de lanzamiento por criatura (`CastMuzzle`), carga (`ChargeMath`),
24 ranuras, reserva de variante de animación por hechizo, contextos y máscaras de postura
por ranura.

Huecos concretos:

- **`SpellType.Trap` y `SpellType.Shield` siguen fuera de la tabla de despacho.**
  Verificado: los tipos enumerados en `Spells/Core/` no los incluyen, así que caen a
  `ProjectileExecutor` con un warning. **No se pueden autorizar en datos.**
- Cuatro banderas de lanzamiento (`allowMovement`, `interruptible`,
  `lockCastDirection`, `allowOverlap`) siguen sin lector.
- `SummonExecutor` produce una criatura sin cerebro FSM completo: `FactionTargeting`
  arregló a quién ataca, pero el invocado sigue naciendo sin `MeleeCombat`.

## 2.3 VFX y lectura del combate — **7.0**

Rigs a medida por familia (`FlameConeFX`, `VortexFunnelFX`, `IceWallVisual`,
`ShieldSphereFX`, `KiAuraFX`), destello de lanzamiento con nueve familias de gesto,
indicador de apuntado en el suelo, anillo en el cursor. La auditoría visual de los 27
hechizos nuevos dejó nueve por debajo de 2.0 y se reconstruyeron; lo que queda es
irregularidad entre familias y **iconos sin hacer** para buena parte del grimorio.

## 2.4 IA hostil — **7.5**

Reconstruida dos veces (5.2 → 7.1 → 8.0 en su propio eje). Percepción con cono, memoria
de última posición vista, grito de alerta, búsqueda, retirada con sondeo geométrico,
tabla de amenaza con vida media, anillo de interposición para que una manada rodee,
eventos de ruido. El techo lo pone el contenido, no el chasis: 17 spawners en todo el
mundo y una sola tabla de botín.

## 2.5 Bosses y encuentros de élite — **2.5**

- **Un solo `BossDefinition` en todo el proyecto**: `SampleBoss.asset`.
- **Un solo monstruo lo referencia**: `barbol_boss.asset`.
- **Ninguna pista de música de boss existe** en `AudioCatalog.asset`, así que
  `musicTrackId` está vacío y todo el cableado de audio por fase no suena nunca.
- Las fases sí soportan `adds`, `desiredRange`, `chaseSpeedMultiplier` y `dodgeChance`
  desde la última pasada de IA — es decir, la capacidad existe y el contenido no.

Un roguelike sin jefes memorables no tiene picos. Esto es andamiaje con una demo encima.

## 2.6 Efectos de estado y control — **7.5**

Nueve efectos (Burn, Poison, Stun, Freeze, Slow, Root, Vulnerable, ThrallMark más la
base), todos por `SpriteTintStack` con capas que multiplican, refresco en vez de apilado,
inmunidades por definición de monstruo. Falta: el jugador apenas tiene acceso a control
de masas y no hay barra de estados legible en el HUD del objetivo.

## 2.7 Botín y recompensa de combate — **3.0**

- **1 de 30 monstruos tiene `lootTable`** (`barbol_loot.asset`). Los otros 29 solo
  sueltan monedas (arreglado en la pasada de economía) y su inventario.
- **No hay cofres ni contenedores**: cero clases `*Chest` / `*Container` en el proyecto.
- Solo **15 objetos equipables** en todo el catálogo de 236 — no hay corriente de mejoras
  que alimente el combate.

Es el eslabón que hace que matar cosas no importe.

---

# Bloque 3 — Progresión y economía (peso 15 %) — 5.7

## 3.1 Progresión de personaje — **7.0**

Auditada en 2.4 y reconstruida: `PlayerStats` con capas que se componen por fórmula
publicada, dos monedas (puntos de talento / puntos arcanos), 9 árboles de hechizos, y
`PlayerStatsWiringTests` cerrando el enum contra sus consumidores. Nivel máximo 60.

Hueco: **hay 6 clases jugables y solo 5 árboles de talento** —
`Data/Progression/SkillTrees/` contiene `barbarian, dwarf, elven, mague, valkyrie`. **La
vampira no tiene árbol**, así que su mitad de progresión de talentos no existe.

## 3.2 Identidad de clase — **5.0**

Las seis clases se diferencian en arte, tamaño (1.80 frente a 2.67 unidades), alcance de
melé y rotaciones de animación. No se diferencian en **verbos**: comparten catálogo de
hechizos, el grimorio es global y la afinidad de clase es solo un recargo de precio.
Ninguna clase tiene un recurso, un mecanismo o una regla propia.

## 3.3 Equipo e inventario — **5.5**

Inventario con ranuras de equipo, arrastrar y soltar, drops en el mundo, integración con
`EquipmentStatSource` que escribe su propia capa. Lo que falta: 15 objetos equipables
totales, `durability` declarado y sin bucle de reparación, sin comparador al pasar el
ratón, sin conjuntos, sin rarezas visibles en el HUD.

## 3.4 Economía y comercio — **6.5**

Reconstruida el 2026-09-07 (3.9 → 7.2): grifo de monedas por monstruo, escalera de
precios por rareza, márgenes por tipo, negociación por persona, ciclo de mercado
determinista con persistencia, monederos de vendedor con restock proporcional.

Lo que la sujeta abajo son los **sumideros**: lo único que se puede comprar es
inventario. El oro no compra poder (talentos y hechizos van por puntos), no hay
reparación, ni viaje rápido, ni almacenamiento, ni respec de pago.

## 3.5 Crafting y profesiones — **4.5**

Cinco oficios declarados, **solo cocina tiene recetas** (36 platos, 41 assets). El
servicio es atómico con rollback, las razones de rechazo son completas, las estaciones
funcionan. Los otros cuatro oficios existen como pestaña vacía.

Peor: `beef`, `potato`, `onion` y `herbs` **no los produce ninguna tabla de
recolección**, así que cocinar es un sumidero puro sin ruta de entrada.

---

# Bloque 4 — Mundo y contenido (peso 15 %) — 4.4

## 4.1 Mundo abierto y zonas — **5.5**

26 zonas de 50×50 compuestas por desplazamiento, 301 edificios colocados, portales entre
zonas, colisión pintada por celda, Y-sort, nueve capas de ordenación para props. Es un
mundo bonito y coherente. Es también **un mundo sin puntos de interés**: no hay mazmorras
señaladas, ni campamentos con recompensa, ni secretos.

## 4.2 Interiores y edificios — **2.5**

1176 plantillas de edificio sobre 1174 sprites, puertas con ancla normalizada,
`WorldTransitionService` como único dueño del intercambio de mundo… y **un solo interior
enviado** (`Maps/Interiors/house_interior_small.overlay.json`). Los interiores son
habitaciones desnudas: sin muebles, sin NPCs, sin botín, sin iluminación propia, un
fichero por puerta y sin anidamiento.

## 4.3 Mazmorras procedurales — **3.0**

`DungeonGenerator` (BSP) más la ruta NodeGraph de Udemy están cableados y activos por
defecto (`_generateBspDungeon = true`). Generan **geometría y nada más**: grep de
`Spawner`, `loot` o `chest` dentro de `World/Dungeon/*.cs` da cero. No hay poblado, ni
salida, ni recompensa, ni motivo para entrar. Para un roguelike, esta es la segunda
carencia más grave después de las misiones.

## 4.4 Día/noche y clima — **6.5**

Reconstruido de 2.0 a 6.4 y ampliado después: ciclo con gradiente de 8 claves, grado de
pantalla en un blit de 0.215 ms, luces derivadas de props, clima por zona con capas de
profundidad, viento compartido, rayos y **nieve que se acumula de verdad** sobre 1176
plantillas sin arte de nieve.

Lo que falta es lo que lo haría importar: **acoplamiento jugable = 0**. La lluvia no moja
el suelo, la nieve no frena, la noche no cambia qué aparece ni cuánto se ve.

## 4.5 NPCs, chat y vida del mundo — **6.0**

Siete personajes con persona doble (runtime más prosa), diario de conversaciones por día,
nueve expresiones faciales con cadena de respaldo, clasificador offline, comercio desde
la conversación, NPC ambiental que pasea y se recoge de noche.

Techo: siete personajes en 26 zonas, sin horarios, sin rutinas, sin reacción a lo que el
jugador hace, y `friendshipScore` sigue sin escritor de producción.

## 4.6 Volumen de contenido — **5.0**

| Contenido | Cantidad |
|---|---|
| Monstruos | 30 definiciones (18 hostiles, 6 oscuros, vendedores) |
| Objetos | 236 (172 materiales, 45 consumibles, 15 equipo, 3 otros, 1 misión) |
| Hechizos | 82 reales más 22 sondas |
| Recetas | 36 (solo cocina) |
| Zonas | 26 más 1 interior |
| Plantillas de edificio | 1176 |
| Pistas de música | 24 |

Suficiente para una demo larga, corto para un juego. El desequilibrio es revelador: 1176
plantillas de decorado contra 15 objetos equipables.

## 4.7 Narrativa y worldbuilding — **2.0**

Hay lore real y recuperado (las siete personas traen trasfondo, voz, límites y estados de
ánimo del build de Python). No hay historia: sin intro, sin premisa, sin conflicto, sin
razón para que el personaje esté ahí. El nombre "Valkur" no significa nada dentro del
juego.

---

# Bloque 5 — Presentación (peso 10 %) — 6.4

## 5.1 Audio — **3.5**

24 pistas de música con metadatos de BPM y **42 entradas de SFX**, de las cuales 22 son
variantes de daño de jugador y 10 de choque de espada. El resto del juego suena a nada.

- **Cero identificadores `spell_*` en el catálogo**, mientras el código llama a 14
  (`spell_slash_swing`, `spell_meteor_impact`, `spell_teleport_arrive`, …). Cada uno es
  un fallo silencioso con un warning por sesión — por eso tres efectos sintetizan su
  sonido en código en vez de usar el catálogo.
- Sin ambiente por bioma, sin pasos, sin sonidos de interfaz más allá de abrir
  inventario, sin música de jefe, sin mezcla dinámica de combate.

Es el eje más barato de subir y el que más cambia la sensación por hora invertida.

## 5.2 UI y HUD — **7.0**

Muy completa: minimapa con marcadores, barras de vida/maná/dash mundiales, ficha de
personaje, reproductor de música con espectro, panel de combos, reloj día/noche, banner
de muerte, barra de hechizos de 24 ranuras, nameplates, HUD de objetivo.

Falta: tooltips comparativos, barra de estados del objetivo, y **el registro de misiones
no se instancia** (ver 1.5).

## 5.3 Menús y opciones — **6.0**

Menú principal con selector de clase, panel de carga con filas ricas, pausa con los
mismos paneles, y tres pestañas de opciones: **Inputs, Sound, Video**. El editor de
controles dibuja teclado y ratón y rebindea por ranura.

Falta: idioma, opciones de jugabilidad, accesibilidad, y no hay pantalla de créditos.

## 5.4 Cámara y game feel — **8.0**

`CameraFeelDirector` con perfil en asset, proxy de seguimiento (nunca se escribe la
cámara), shake/kick/lead/trauma, escalón de PPU respetado, `AspectRatioEnforcer`
cuantizando a ratio entero, hit-stop. Documentado hasta la regla de por qué no existe un
punch de zoom legal a 16 PPU.

## 5.5 Arte y pipeline — **7.5**

Cuatro pipelines completas y reproducibles (jugadores de 2 direcciones, monstruos wave13,
props y edificios, iconos), manifiestos como registro, tests que comparan el manifiesto
contra el disco en ambos sentidos, 9 atlas con un solo dueño.

Riesgo real: los atlas son **sin comprimir a propósito**, y los de personajes van por
**320 MB de VRAM** tras subir tres personajes a 256 px. Es la factura que hay que citar
antes de subir un cuarto.

---

# Bloque 6 — Producción e ingeniería (peso 10 %) — 5.8

## 6.1 Entrada y controles — **8.5**

Un solo modelo de binding (el asset), contextos (guerra, paz, editor), máscaras de
postura por acción, escáner de conflictos consciente del contexto, editor de controles
dibujado, persistencia por id de binding, dos guardias de test que prohíben leer un
dispositivo o declarar un binding en C#.

## 6.2 Guardado y persistencia — **8.0**

Escritura atómica con temp por GUID, checksum, 5 copias rotativas, migración de esquema,
bolsa de metadatos para estado de mundo (mercado, muerte), base de perfil detrás de
`IProfileDb`. Documentado hasta el detalle de que `File.Replace` de Mono no es atómico.

## 6.3 Muerte y respawn — **8.0**

Reconstruido de 2.8 a 8.1 el mismo día: altares como propiedad del edificio con cuatro
anclas, dos relojes de rescate independientes, estado de espíritu persistido, rastro
doble, sondas de consola. Lo que queda abierto es extensibilidad: sin checkpoints, sin
altar por interior ni por mazmorra.

## 6.4 Arranque y carga — **8.5**

De 11 286 ms a **3 681 ms** en una sesión, con la barra dejando de mentir: secuencia como
datos (64 pasos), pesos automedidos, una sola frontera de excepción, watchdog por latido,
precarga de assets mientras el menú está abierto.

## 6.5 Editores de autoría — **8.5**

17 editores en runtime bajo una capa de espacio de trabajo persistente, tema único con
trinquete de colores crudos, zoom y paneo en los 11 que pueden, chrome compartido, test
de contrato que impide que un editor nuevo se salte la capa. Es la mejor pieza de
ingeniería del proyecto y explica por qué el contenido puede crearse rápido cuando
alguien se siente a crearlo.

## 6.6 Tests y QA — **8.0**

753 ficheros EditMode y 17 PlayMode; **7 057 `[Test]`, 494 `[TestCase]`, 113
`[UnityTest]`**. Suite completa en unos 126 s tras la virtualización del editor de
objetos. Muchos son guardias estructurales (fuente escaneada, manifiesto contra disco,
datos enviados) y no solo unitarios, que es la clase que atrapa los fallos de
*composición*.

Hueco: **17 ficheros PlayMode** contra 753 de EditMode. Casi nada se prueba jugando, y
los defectos más caros de este proyecto (el ratón congelado, la deriva del spawner, el
altar invisible) fueron todos de Play Mode.

## 6.7 Rendimiento — **5.5**

El arranque está medido y resuelto; `SpriteMeshType.Tight` y la tabla del editor de
objetos también. Pero **no hay ninguna medición reciente de FPS en juego**, ni un
presupuesto de frame, ni un perfil con 30 monstruos y clima activo. La VRAM de atlas
(320 MB) no está presupuestada contra ninguna máquina objetivo.

## 6.8 Build, release y plataformas — **1.5**

- **No hay CI**: `.github/workflows/` no existe.
- Tres escenas en build settings y nada más: sin perfiles de plataforma, sin firma, sin
  versionado, sin pipeline de empaquetado.
- No hay constancia de que se haya generado nunca un player, así que todo lo que solo
  falla fuera del editor (`Resources` recortado, código eliminado por el stripper, rutas
  de `StreamingAssets`) está sin verificar.

## 6.9 Localización — **1.0**

Cero infraestructura. `ChatLanguage` es una clase de cadenas del chat, no un sistema.
La interfaz mezcla español ("Conversar", "Sin conflictos reales") e inglés ("Inputs",
"Sound", "Video") en la misma pantalla. Cada cadena está incrustada en su sitio de uso.

## 6.10 Accesibilidad — **0.5**

Cero: sin modo daltónico, sin escala de interfaz, sin subtítulos, sin opción de reducir
el shake, sin remapeo de doble pulsación. Lo único que cuenta a favor es que el rebinding
de teclas y ratón es completo y persiste.

---

# Qué hacer, por apalancamiento

El orden es por *nota global movida por hora invertida*, no por dificultad.

## Nivel 1 — convierte el motor en un juego

1. **Enviar misiones.** El código está escrito; faltan los datos y tres cables: un
   componente dador en el NPC, instanciar `QuestLogHUD` en el arranque, y un campo de oro
   en `QuestDefinition`. Diez misiones de matar, recolectar y entregar encadenan NPCs,
   economía, botín y mundo de golpe. *(Eje 1.5: 1.0 → 6.0)*
2. **Poblar la mazmorra generada.** La geometría ya se genera. Añadir spawners por sala,
   una sala de jefe, un cofre y una salida. Es lo que da estructura de run.
   *(4.3: 3.0 → 6.5)*
3. **Objetivo y final.** Aunque sea uno: derrotar al jefe de la mazmorra cierra la run,
   escribe el registro y devuelve al menú. *(1.2: 0.5 → 5.0)*
4. **Tabla de botín para los 29 monstruos restantes**, y cofres. Sin esto, matar no
   recompensa. *(2.7: 3.0 → 6.5)*

## Nivel 2 — hace que se sienta terminado

1. **Sonido.** 30–40 SFX bien elegidos: los 14 `spell_*` que el código ya invoca, pasos,
   impactos, interfaz, y una pista de jefe. Es el cambio de sensación más barato del
   proyecto. *(5.1: 3.5 → 7.0)*
2. **Un tutorial de cinco minutos.** No una capa nueva: los prompts de interacción y el
   badge ya existen; basta una secuencia guiada en la zona inicial. *(1.4: 0.5 → 5.5)*
3. **Segundo y tercer jefe**, con fases que ya soporta `BossDefinition`, más música.
   *(2.5: 2.5 → 6.0)*
4. **Árbol de talentos de la vampira** — la única clase sin él. *(3.1: 7.0 → 8.0)*
5. **Sumideros de oro**: reparación, viaje rápido, almacenamiento, respec de pago.
   Desbloquea el eje de economía que ya está construido. *(3.4: 6.5 → 8.0)*

## Nivel 3 — riesgo de producción

1. **Generar un player y medirlo.** Es la única forma de saber qué se rompe fuera del
   editor, y ahora mismo es una incógnita total. *(6.8: 1.5 → 5.0)*
2. **CI mínima**: compilar y correr EditMode en cada push. La suite ya existe y hoy solo
   corre cuando alguien se acuerda.
3. **Un perfil de frame en juego** con 30 monstruos, clima y noche. Sin número, cualquier
   optimización futura es una conjetura. *(6.7: 5.5 → 7.5)*

## Nivel 4 — amplitud

1. Localización: extraer cadenas y elegir un solo idioma coherente para empezar.
2. Accesibilidad: daltonismo, escala de interfaz, reducir shake.
3. Interiores poblados; oficios más allá de la cocina; identidad mecánica por clase;
   acoplamiento jugable del clima.

---

## Nota sobre el método

Cada nota de este documento se apoya en un conteo o un grep sobre el árbol enviado, no
sobre lo que el código dice de sí mismo. Es la misma disciplina que ya evitó tres
desastres en este proyecto (la deriva del spawner, el altar con el id equivocado, la
barra de carga que llegaba al 100 % en el 75 % del trabajo): **una cifra puede ser real,
internamente consistente, y hablar de otra cosa distinta de la que preguntaste.** Lo que
se midió aquí fue siempre "cuántos de estos hay en el disco", nunca "existe el sistema".
