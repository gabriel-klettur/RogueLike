# Auditoría del sistema de talar árboles — 2026-09-13

**Nota global: 5.2 / 10.** La ingeniería es de 8; el juego que produce es de 3.

El núcleo (`HarvestNode`, `BuildingDurability`, `HarvestBlowResolver`, `WorldDamageService`)
está bien separado, documentado, barato en rendimiento y persiste bien. Lo que falla está
encima: el balance de recompensas tiene un exploit que convierte el hacha en la PEOR
herramienta, la caída del árbol no existe (el sprite salta a tocón), talar es mudo, la
profesión de leñador no gana nada con talar, y la madera no la consume ningún sistema.

Auditoría de sólo lectura. Nada se ha modificado. Los 71 tests del sistema **no se pudieron
ejecutar**: el Editor estaba en Play Mode (probablemente otra sesión) y no se detuvo.

## Qué hay

```text
Data/World/Destruction/        DestructionProfile, DestructionResistanceTable, HarvestDropTable,
                               DamageClass, MaterialClass, HarvestMode, DestructionKind
Gameplay/World/Harvesting/     13 ficheros, ~2 500 líneas
  HarvestNode (+.Session, .Swing)   sesión con la tecla, prompt, spent/regrow
  BuildingDurability                vida del edificio, muerte, tocón, regrow
  HarvestBlowResolver               matriz + gate de herramienta (dueño único)
  DamageClassResolver               qué herramienta equipada cuenta
  HarvestDropResolver               tabla independiente y pool ponderado
  HarvestFeedback / HarvestNodeBar  astillas, destello, cámara, "+1", barra
  HarvestSwingRegistry              golpes de swing a vetas (Deplete)
  WorldDamageService / Flusher      persistencia por run
Player/PlayerController.Harvest.cs  animación de trabajo, pacing derivado del intervalo
```

Datos enviados, medidos:

| Hecho | Valor |
|---|---|
| Perfiles de destrucción | **3** (`DP_tree_common`, `DP_mine_iron`, `DP_fish_school`) |
| Plantillas que usan `DP_tree_common` | **553** (416 `nature/`, 137 `vegetation/`) — todas árboles |
| Árboles talables colocados en el mundo | **88** de 324 edificios |
| Tocón | **uno** (`tree_stump_cut_rings`) para los 553 árboles, palmeras y pinos incluidos |
| `regrowSeconds` del árbol | **0** — un árbol talado no vuelve en toda la run |
| Pool por golpe (`LT_lumberjack_yield`) | **64** ítems `wood_01..wood_64`, todos "Madera", precio 2, rareza 0, icono distinto |
| Drop final (`HDT_wood_common`) | `wood` x2-4, un 65.º ítem de madera |
| Recetas o misiones que consumen madera | **0** |
| Herramientas | `axe_iron` tier 1 (sólo la vende el herrero, 60 monedas) |
| Sonidos `harvest_*` en `AudioCatalog` | **0** |
| Personajes con arte `harvest_chop` | **1 de 6** (dwarf, y su gemelo dark) |
| Tests | 71 en 6 fixtures |

## Notas por aspecto

| # | Aspecto | Nota |
|---|---|---|
| 1 | Arquitectura y separación de responsabilidades | **8.5** |
| 2 | Robustez del ciclo de vida y estados | **8.0** |
| 3 | Rendimiento | **8.5** |
| 4 | Persistencia | **7.5** |
| 5 | Coherencia de reglas (matriz, herramienta, dos vías de entrada) | **6.0** |
| 6 | Balance y economía de la recompensa | **2.0** |
| 7 | Game feel del golpe | **5.0** |
| 8 | Belleza de la tala (caída, tocón, rebrote) | **3.0** |
| 9 | Animación del personaje | **5.0** |
| 10 | Audio | **1.0** |
| 11 | UX y claridad (prompt, avisos, idioma) | **6.5** |
| 12 | Progresión (profesión, herramientas, tiers) | **1.5** |
| 13 | Integración con otros sistemas (IA, misiones, crafting) | **2.5** |
| 14 | Variedad de contenido | **3.0** |
| 15 | Autoría y herramientas de diagnóstico | **2.5** |
| 16 | Tests y verificabilidad | **7.0** |
| 17 | Escalabilidad a nuevos recursos | **7.5** |
| 18 | Documentación en código | **7.0** |

### 1. Arquitectura — 8.5

- Dos verbos separados a propósito (`Destroy` vs `Deplete`), y los dos comparten UNA respuesta a
  "cuánto vale este golpe" (`HarvestBlowResolver`). Es exactamente la forma correcta.
- `HarvestSwingRegistry` en vez de `IDestructibleObstacle` para vetas: razonado y documentado
  (un proyectil perdido no vacía una mina).
- `IWorkProgress` hace la barra reutilizable (pesca ya la usa).
- Resta: `HarvestNode.cs` 522 líneas, por encima del tope de ~250 del proyecto.

### 2. Robustez — 8.0

- Regrow en reloj de pared, deadline limpiado ANTES de cambiar estado (bug medido y arreglado).
- Restauración con clamp; `RestoreSpent` para no reiniciar temporizadores.
- Estáticos con reset `SubsystemRegistration`; registros que se purgan al morir.
- Tabla de resistencia ausente = permisivo, no mundo invencible.
- Resta: un `instanceId` reutilizado tras editar el mapa aplicaría el registro de daño de otro
  edificio (la clave es `slot|zone|instanceId`, sin plantilla ni versión).

### 3. Rendimiento — 8.5

- `enabled = false` salvo sesión o rebrote pendiente: 88 árboles no cuestan `Update`.
- Feedback y barra se construyen perezosos al primer golpe.
- Guardado coalescido cada 5 s, más flush al salir y en `OnDisable`.
- Resta: `HarvestFeedback` crea un `ParticleSystem` y un sprite POR ÁRBOL tocado y nunca los
  libera; talar 88 árboles deja 88 sistemas de partículas vivos. Un pool compartido costaría
  menos.

### 4. Persistencia — 7.5

- Nunca escanea la escena, así que no puede grabar un mundo vacío. Diseño correcto.
- Daño parcial, talado y deadline sobreviven a recargas; `Flush` rechaza EditMode.
- Resta: sólo `WorldId.Base`; esquema `1` sin migración declarada; la clave frágil del punto 2.

### 5. Coherencia de reglas — 6.0

Hay **dos formas** de talar y **no producen lo mismo**:

| Vía | Rollo del pool por golpe | Astillas / destello / "+1" / barra | Drop final |
|---|---|---|---|
| Tecla de interacción (`LandBlow`) | sí | sí | sí |
| Swing o hechizo (`SlashAttack` → `BuildingDurability`) | **no** | **no** (nadie escucha `Struck` salvo el guardado) | sí |

Talar a tajos es mudo hasta que el árbol desaparece y paga ~3 maderas; talar con la tecla paga
el pool en cada golpe. Mismo árbol, misma herramienta, dos economías.

Además: `Projectile` alcanza árboles por `GetComponentInParent<IDestructibleObstacle>` y la
madera tiene fuego 1.4. Sin verificar en vivo, pero por construcción un monstruo lanzador que
falle un fuego deforesta el mapa de forma permanente (`regrowSeconds: 0`).

### 6. Balance y economía — 2.0 (el hallazgo principal)

**El número de golpes lo decide la herramienta; la recompensa por golpe no.** `LandBlow` en modo
`Destroy` tira el pool en CADA golpe no inmune, independientemente del daño hecho. Con
durabilidad 40 y `blowDamage` 10:

| Herramienta | Multiplicador | Daño/golpe | Golpes por árbol | Maderas por árbol |
|---|---|---|---|---|
| Hacha de hierro | 1.00 | 10 | 4 | 4 + 2-4 |
| Espada de hierro (blade, tier 1) | 0.45 | 4 | 10 | 10 + 2-4 |
| Manos desnudas | 0.10 × 0.15 = 0.015 → suelo 1 | 1 | **40** | **40 + 2-4** |

- La tasa por segundo es idéntica (1 madera cada 0.6 s) con cualquier herramienta, así que el
  hacha **no acelera el ingreso**; sólo agota el árbol antes.
- Los árboles son finitos (88, sin rebrote), así que **talar a mano extrae ~10x más madera del
  mundo** que con hacha. El aviso "así es muy lento" empuja al jugador hacia la opción peor.
- Techo aproximado por run: hacha ~600 maderas; manos ~3 800 (≈ 7 600 monedas a precio 2,
  frenado sólo por el `coinFloat` de 400 del leñador).
- 64 ítems de madera distintos con el mismo nombre, precio y descripción: cada golpe puede abrir
  una pila nueva en la mochila. Es ruido de inventario sin decisión detrás.
- Ningún sistema consume madera: su único destino es vender.

Arreglo de raíz: la recompensa se paga por **trabajo realizado**, no por golpe — por ejemplo,
una tirada por cada N puntos de durabilidad arrancados (lo mismo que `ApplyWork` ya hace para
las vetas con `_chargeProgress`), aplicado en `BuildingDurability.ApplyObstacleDamage` para que
las dos vías coincidan.

### 7. Game feel del golpe — 5.0

- Bien: astillas opacas del color del material, destello aditivo, `CameraFeel` distinto para
  golpe inútil, "+1" con lo realmente soltado, barra que sólo aparece trabajando.
- Mal: el **árbol no reacciona** (ni sacudida, ni inclinación, ni hojas cayendo); el contacto se
  dibuja en el centro del tronco, no en el lado desde el que se golpea; nada de hit-stop; la vía
  de swing no tiene ningún feedback (punto 5).

### 8. Belleza de la tala — 3.0

- `DestructionKind` (`Fell`, `Shatter`, `Crumble`, `Collapse`) tiene **cero lectores**: el árbol
  no cae, se sustituye por el tocón en un fotograma. Otro campo autorado e inerte.
- Un único tocón de anillos para 553 árboles: una palmera tropical y un pino dejan el mismo
  tocón de roble.
- Sin rebrote, así que no hay transición de vuelta que embellecer.
- No hay partículas de hojas, polvo de caída ni sombra que se desplome.

### 9. Animación del personaje — 5.0

- Muy bien resuelto donde existe: variante reservada `harvest_chop`, ciclo ajustado al
  intervalo de golpe para que los fotogramas profundos se vean.
- Sólo el enano tiene arte; los otros cinco usan la rotación de ataque normal.

### 10. Audio — 1.0

Cero sonidos `harvest_*` en el catálogo. El código está preparado (con `HasSfx`), pero talar es
**silencioso**. El proyecto ya sintetiza audio de clima, hielo, escudo y bumerán; aquí ni eso.
Tampoco hay sonido de caída.

### 11. UX y claridad — 6.5

- Bien: prompt con verbo ("Talar"), aviso de herramienta por material ("Necesitas un hacha"),
  cuenta atrás de rebrote en m:ss, huella en vez de copa para el rango.
- Mal: `HarvestFeedback` escribe **"Wrong tool"** e **"Immune"** en inglés en un juego en español;
  el aviso de lentitud es engañoso (punto 6); el hacha ocupa la **única** ranura `Weapon`, así
  que para talar hay que quitarse el arma y el comentario de `DamageClassResolver` ("llevar hacha
  y pico a la vez") ya no es posible con el equipo tipado.

### 12. Progresión — 1.5

- La profesión `lumberjack` existe (tab, curva) pero **talar no da XP**: el único
  `PlayerProfessions.AddXp` es crafting.
- Un solo tier de hacha; sin tiers de árbol (el tooltip propone brote 15 / común 40 / ancestral
  120, pero sólo existe el perfil de 40).
- Sin talentos, sin rendimiento por nivel, sin maderas raras.

### 13. Integración — 2.5

- `noiseRadius: 12` tiene **cero lectores**: talar no alerta a nadie aunque `NoiseEvents` existe
  y es exactamente para eso.
- Sin eventos de juego (`GameEvents`) de "árbol talado": ninguna misión puede pedirlo.
- Madera sin consumidor en crafting ni misiones.
- Bien: minimapa ya tiene icono de hacha para el leñador; persistencia enchufada al arranque.

### 14. Variedad de contenido — 3.0

Un perfil para 553 árboles, un tocón, una tabla de drops, un hacha, una animación. La matriz de
materiales es rica (6 materiales × 11 clases) y apenas se aprovecha.

### 15. Autoría y diagnóstico — 2.5

- `DestructionProfile` sólo se edita en el Inspector; el editor de Buildings no muestra si una
  plantilla es talable, su perfil ni su estado.
- Ningún comando de consola (`harvest`, `regrow`, `fell`): verificar un rebrote obliga a esperar.
- Nada dibuja la huella de interacción ni el radio de ruido.

### 16. Tests — 7.0

- 71 tests: resolvedor, nodo, animación, persistencia, datos enviados.
- Ninguno compone **golpes × recompensa × herramienta**, que es donde vive el exploit; los
  fixtures prueban cada mitad por separado, la forma que este proyecto ya ha documentado varias
  veces.
- No ejecutados en esta auditoría (Play Mode activo).

### 17. Escalabilidad — 7.5

- Un recurso nuevo es datos: perfil + tabla + clave de animación, sin código. Cuesta poco sumar
  arbustos, rocas o cajas.
- Límites: el "ruido" y la "caída" no escalan porque no existen; el pool por golpe no escala en
  economía; ranura única de arma para herramientas.

### 18. Documentación — 7.0

Comentarios del "por qué" de muy alto nivel, con incidentes medidos. Pero hay texto caducado que
miente:

- `DestructionProfile.blowDamage` dice "en Deplete un golpe cuesta exactamente una carga"; hoy
  `ApplyWork` acumula trabajo.
- `BuildingDurability` y `DestructionProfile` citan "969 plantillas"; son 1 176.
- `DamageClassResolver` describe el equipo como "rejilla plana 3x3"; hoy son 8 ranuras tipadas.

## Hoja de ruta propuesta (por impacto)

1. **Recompensa por trabajo, no por golpe**, en el camino común de `BuildingDurability`; mismo
   pago por tecla, swing y hechizo. Test de composición: hacha ≥ manos en maderas por árbol.
2. **Talar da XP de `lumberjack`**, y la madera obtiene un consumidor (receta o misión).
3. **Caída del árbol**: leer `DestructionKind.Fell` — inclinación y desplome del canopy, polvo,
   hojas, sacudida de cámara al tocar suelo; sacudida del árbol en cada golpe.
4. **Audio sintetizado** de golpe en madera y de caída (patrón `BoomerangAudio`).
5. Traducir "Wrong tool"/"Immune"; corregir el aviso de lentitud.
6. **`noiseRadius` → `NoiseEvents`**; evento "árbol talado" para misiones.
7. **Perfiles por tipo** (brote / común / ancestral) y tocones por familia (pino, palmera,
   frondoso); `regrowSeconds` > 0 para que el oficio sea sostenible.
8. Colapsar las 64 maderas en pocos tipos con significado (o en una) y ranura de herramienta
   separada del arma.
9. Consola `harvest` / `regrow` y panel de destructibilidad en el editor de Buildings.
10. Pool compartido para las astillas; partir `HarvestNode.cs`; limpiar comentarios caducados.

Con 1-5 hechos la nota estimada sube a ~7.5; con todo, ~8.5.

---

## Después de la implementación (mismo día)

Implementado: habilidad de Tala 0-100 %, recompensa por trabajo, 15 tipos de madera por
habilidad y especie, 10 familias de árbol, rebrote, caída animada, sacudida, partículas
compartidas con textura, ruido, eventos, dos tipos de objetivo de misión, panel OFICIOS,
consola y tests de composición. El audio se omitió a petición. Detalle técnico en CLAUDE.md,
sección «Woodcutting: a 0-100 % skill, paid by work».

Verificado: suite EditMode completa 9028 tests en verde (la única roja,
`BuildingProjectedShadow`, venía de otra sesión y se corrigió con `[SelfHealingStatic]`),
consola limpia, y en Play: talar con la sesión real (5 golpes, 5 maderas, +0.3 %), caída
fotografiada, rebrote de 0.2 a 1.0, panel OFICIOS capturado, informe `talar` a 60 % en un
árbol tropical (eficiencia x1.53, 7 tipos de madera posibles).

| # | Aspecto | Antes | Después | Qué lo limita |
|---|---|---|---|---|
| 1 | Arquitectura | 8.5 | **9.0** | — |
| 2 | Robustez | 8.0 | **8.5** | clave `slot\|zona\|instanceId` sin versión |
| 3 | Rendimiento | 8.5 | **9.0** | — |
| 4 | Persistencia | 7.5 | **8.5** | ídem 2 |
| 5 | Coherencia de reglas | 6.0 | **9.0** | — |
| 6 | Balance y economía | 2.0 | **8.5** | la madera no tiene consumidor en crafting |
| 7 | Game feel | 5.0 | **8.0** | sin sonido, sin hit-stop propio |
| 8 | Belleza de la tala | 3.0 | **8.0** | un único arte de tocón (tintado por familia) |
| 9 | Animación | 5.0 | 5.0 | arte de talar solo para el enano |
| 10 | Audio | 1.0 | 1.0 | omitido a petición |
| 11 | UX y claridad | 6.5 | **8.5** | — |
| 12 | Progresión | 1.5 | **8.5** | un solo tier de hacha |
| 13 | Integración | 2.5 | **8.0** | sin receta que use madera |
| 14 | Variedad | 3.0 | **7.5** | el mundo solo coloca 3 de las 10 familias |
| 15 | Autoría y diagnóstico | 2.5 | **7.0** | sin panel en un editor runtime |
| 16 | Tests | 7.0 | **8.5** | — |
| 17 | Escalabilidad | 7.5 | **9.0** | minería y pesca aún no usan la capa de habilidad |
| 18 | Documentación | 7.0 | **8.5** | — |

**Global: 8.2 / 10 sin contar el audio** (7.8 contándolo).

Lo que falta para acercarse a 10, por impacto: colocar arboledas de las familias difíciles en
el mundo (el fichero del mundo lo estaba editando otra sesión), arte de talar para las otras
cinco clases, sonido, una receta que consuma madera, un panel de autoría en un editor runtime,
y pasar minería y pesca a la misma capa de habilidad.
