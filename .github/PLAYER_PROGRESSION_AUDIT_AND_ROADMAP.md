# Auditoría — Stats, Skills y Progresión del Jugador

> Auditoría: 2026-09-03 · Implementación: 2026-09-04 · Alcance: todo lo que define
> **qué es un personaje** en Valkur y **cómo crece**. Todo lo que aparece aquí está medido
> sobre el árbol, no inferido.
>
> **ESTADO: fases 0-4 implementadas.** La auditoría de abajo se conserva tal cual como
> registro de lo que se encontró; lo que se construyó está en la sección final,
> *Lo que se implementó*.

## Resumen ejecutivo

Valkur tiene **la arquitectura de progresión escrita y los datos vacíos**. No es que esté mal
diseñada: es que casi nada de ella llega a un píxel. El patrón se repite once veces con la
misma forma que CLAUDE.md ya documentó para el chat ("225 tests verdes sobre nada") y para
`animation_map.json` ("autorizado, serializado e inerte").

Números duros:

| Medida | Valor |
|---|---|
| Campos en `PlayerDefinition` | 14 |
| Campos que llegan al juego | **5** (36 %) |
| Assets `SkillNode` / `SkillTree` en el proyecto | **0** |
| Componentes `LearnedSkills` añadidos al jugador | **0** |
| Assets `QuestDefinition` | **0** |
| Assets `XpCurveDefinition` / `LevelStatCurve` | **0** |
| Hechizos registrados al jugador al aparecer | **77 de 77** (sin desbloqueo) |
| Hechizos con `manaCost: 0` | **46** |
| Ítems equipables con stats de combate | 15 (ninguno leído por el combate) |
| Stats del jugador que crecen al subir de nivel | **0** |
| Daño melé del jugador, nivel 1 y nivel 50 | **1–2** (constante) |
| Defensa del jugador | **0** (nunca se llama `Health.SetDefense`) |

**Nota global ponderada: 2.4 / 10.**

## Puntuaciones por eje

| # | Eje | Nota | Veredicto en una línea |
|---|---|---|---|
| 1 | Modelo de datos del personaje | 2 | 14 campos, 5 vivos, dos modelos rivales (`PlayerDefinition` vs `EntityStats`) |
| 2 | Diferenciación entre las 5 clases | 2 | Solo difieren en HP, maná y velocidad; el resto de la ficha es decorativo |
| 3 | Progresión por nivel (XP) | 3 | El nivel sube y **no otorga nada**: sin stats, sin puntos, sin desbloqueos |
| 4 | Árbol de habilidades | 1 | Capa completa (6 ficheros + HUD + tests) con **cero nodos y cero cableado** |
| 5 | Hechizos como progresión | 1 | Los 77 se entregan a la vez en el segundo 0 de la partida |
| 6 | Itemización / equipo | 1 | `damage`, `critChance`, `attackSpeed` existen y **ningún sistema de combate los lee** |
| 7 | Combate cuerpo a cuerpo | 2 | Daño fijo 1–2 de por vida; irrelevante frente a hechizos de 45 |
| 8 | Recursos (HP / maná / energía / hambre) | 4 | HP y maná funcionan; `Energy` y `Hunger` nunca se añaden al jugador |
| 9 | Consumibles y buffs | 3 | Curación y maná funcionan; el buff temporal es literalmente un `Debug.Log` |
| 10 | Misiones como progresión | 1 | Sistema correcto, 0 assets, y sus recompensas apuntan a componentes ausentes |
| 11 | Economía y recompensas | 2 | Oro y loot existen; nada que comprar mejora al personaje |
| 12 | Persistencia de la progresión | 5 | Guarda hp/maná/xp/nivel/inventario; no guarda skills porque no hay |
| 13 | Legibilidad para el jugador (ficha) | 2 | La pestaña "STATS" muestra estadísticas de perfil, no del personaje |
| 14 | Calidad del código de la capa | 7 | Bien escrito, bien documentado, con costuras correctas — solo que sin datos |
| 15 | Tests frente a la realidad | 3 | Verdes sobre sistemas que el juego real nunca instancia |
| 16 | Simetría jugador ↔ monstruo | 2 | El monstruo tiene defensa, resistencias, inmunidades y escalado; el jugador ninguna |

---

## 1. Modelo de datos del personaje — 2/10

`PlayerDefinition` ([PlayerDefinition.cs](../unity/Valkur/Assets/_Project/Scripts/Data/Player/PlayerDefinition.cs))
declara 14 campos. Lectores reales en gameplay:

| Campo | ¿Vivo? | Quién lo lee |
|---|---|---|
| `maxStrength` | ✅ | `EntitySetup.InitHealth` → HP máximo |
| `maxIntelligence` | ✅ | `Mana.Initialize` → maná máximo |
| `manaRegenPerSecond` | ✅ | `Mana.Initialize` |
| `basicSpeed` | ✅ | `InitPlayerMovement` |
| `basicAttack` | ✅ | `MeleeCombat.Initialize(def.basicAttack, 0.5f, 1.5f)` |
| `maxDexterity` | ❌ | nadie |
| `initialStrength` / `initialIntelligence` / `initialDexterity` | ❌ | nadie |
| `basicArmor` | ❌ | nadie — el jugador nunca recibe `Health.SetDefense` |
| `basicDeathTimerDuration` | ❌ | nadie |
| `damageStopProbability` | ❌ | solo la versión de `EntityStats`, y solo para monstruos |
| `dashCharges` | ❌ | `DashAbility` no tiene concepto de cargas |
| `dragDropRange` | ❌ | nadie (y vale `128` — píxeles de Python, la sexta aparición de ese error de unidades) |

**Dos modelos rivales.** El monstruo usa `EntityStats` (18 campos: defensa, resistencias
elementales, inmunidades a estados, rangos, cooldowns). El jugador usa `PlayerDefinition`,
que no tiene ninguno de esos. No hay un tipo común, así que cualquier sistema que quiera
tratar a los dos por igual — mitigación, resistencias, escalado — tiene que elegir uno y
dejar fuera al otro. Hoy elige al monstruo.

El nombre de los tres atributos también es una deuda heredada: **`maxStrength` *es* el HP** y
**`maxIntelligence` *es* el maná**. No son atributos, son recursos con nombre de atributo. Un
diseñador que suba "fuerza" esperando pegar más fuerte sube la vida.

## 2. Diferenciación de clases — 2/10

Las cinco clases (`dwarf`, `barbarian`, `elven`, `mague`, `valkyrie`) autorizan valores
distintos en los 14 campos, pero solo 5 llegan al juego, así que la diferencia real es:

| Clase | HP | Maná | Velocidad | Daño melé | Todo lo demás |
|---|---|---|---|---|---|
| dwarf | 200 | 35 | 4 | 1 | idéntico |
| barbarian | 150 | 25 | 5 | 2 | idéntico |
| elven | 70 | 55 | 6 | 2 | idéntico |
| mague | 100 | 100 | 5 | 1 | idéntico |
| valkyrie | 90 | 35 | 7 | 2 | idéntico |

Las cinco lanzan **exactamente los mismos 77 hechizos**, con los mismos cooldowns y el mismo
daño. El mago no lanza mejor; el enano no aguanta más golpes (defensa 0 en los cinco). Y como
**46 hechizos cuestan 0 de maná**, el pozo de maná — la única estadística en la que las clases
difieren de verdad (25 vs 100, factor 4) — casi nunca es la restricción.

Los `initial*` son idénticos en las cinco (45/45/45, salvo la destreza del mago) y nadie los
lee, lo que confirma que se importaron de Python sin destino.

## 3. Progresión por nivel — 3/10

La cadena existe entera y termina en el vacío:

```text
matar → DeathDropSystem.ResolveXpReward → Experience.AddXp → nivel++
     → GameEvents.OnLevelUp → LevelUpRestoreSystem      (cura)          ✅ funciona
                            → LevelUpStatScalingSystem  (curve == null) ❌ no hace nada
                            → LevelUpSkillPointSystem   (sin LearnedSkills) ❌ no hace nada
```

- `Experience` calcula el requisito como `100 * N^1.5` inline porque **no existe ningún asset
  `XpCurveDefinition`**. Sin curva no hay tope de nivel (`IsAtLevelCap` es `false` siempre).
- `LevelUpStatScalingSystem` se crea en el bootstrap y su `curve` es `null` — **no existe
  ningún asset `LevelStatCurve`**. Cero HP y cero maná por nivel.
- `LevelUpSkillPointSystem` se crea en el bootstrap y busca `LearnedSkills` en la entidad que
  subió de nivel. El jugador no lo tiene. Sale en silencio, por diseño documentado.

**Consecuencia medible:** subir de nivel 1 → 50 cura al jugador y no cambia ni un solo número.
El único efecto observable de la XP es el texto flotante y los orbes.

Los datos de XP tampoco están cerrados: de 19 monstruos, **6 llevan `xpReward: 0`** y caen al
fallback heredado `hp/5 + power`, mientras el resto va de 5 a 2500.

## 4. Árbol de habilidades — 1/10

Lo que existe, y está bien escrito:

- [SkillNode.cs](../unity/Valkur/Assets/_Project/Scripts/Data/Combat/SkillNode.cs) — id estable,
  coste, requisito de nivel, prerrequisitos, efectos `(kind, key, value)`. La decisión de
  evitar herencia está razonada en el propio doc-comment.
- [SkillTree.cs](../unity/Valkur/Assets/_Project/Scripts/Data/Combat/SkillTree.cs) — contenedor
  con lookup hasheado perezoso.
- [LearnedSkills.cs](../unity/Valkur/Assets/_Project/Scripts/Gameplay/Player/LearnedSkills.cs) —
  set aprendido, puntos, `CanLearn` con `out string reason`, snapshot de guardado.
- [SkillEffectApplicator.cs](../unity/Valkur/Assets/_Project/Scripts/Gameplay/Player/SkillEffectApplicator.cs) —
  puente a `Health` / `Mana` / `SpellCaster` / `AuraRegistry`, con costura `ReapplyAll` para la carga.
- [SkillTreeHUD.cs](../unity/Valkur/Assets/_Project/Scripts/Gameplay/HUD/SkillTreeHUD.cs) + pestaña
  "SKILLS" en `CharacterSheetController`.
- Tests: `LearnedSkillsTests`, `SkillEffectApplicatorTests`, `LevelUpSkillPointSystemTests`.

Lo que falta, y lo anula todo:

1. **Cero assets.** `grep` por los GUID de `SkillNode` y `SkillTree` sobre todos los `.asset`,
   `.prefab` y `.unity` del proyecto: ningún resultado.
2. **`LearnedSkills` nunca se añade al jugador.** No hay un solo `AddComponent<LearnedSkills>`
   en el proyecto. `EntitySetup.ConfigurePlayerStats` no lo instancia.

El HUD abre y sale por `if (skills == null || skills.Tree == null) return`, así que la pestaña
"SKILLS" de la ficha de personaje muestra un panel vacío sin explicar por qué. Los efectos
`StatBoost` solo reconocen dos claves (`maxHp`, `maxMana`) — no hay forma de expresar "+daño",
"+velocidad de ataque", "+crítico", porque no existen componentes de stat que lo reciban.

Es exactamente la forma del incidente del chat: **suite verde sobre un sistema que la partida
real nunca instancia**.

## 5. Hechizos como progresión — 1/10

`EntitySetup` registra **los 77 `SpellDefinition` del catálogo** en el `SpellCaster` del
jugador en el instante en que aparece:

```csharp
foreach (var spell in allSpells) { caster.RegisterSpell(spell.spellKey, spell); registered++; }
```

No hay lista de conocidos, ni desbloqueo, ni requisito de nivel, ni coste. El `SpellBarHUD`
rellena la barra desde los slots y luego desde el libro completo. El `SkillEffectKind.UnlockSpell`
existe para desbloquearlos y no tiene ningún nodo que lo use.

Del reparto por `audience`: 46 son lanzables por el jugador (44 `Player` + 2 `Player|Boss`),
8 de NPC, 1 de jefe, y 22 con `audience: None` (los 19 sondeos `anim_*` más algunos sueltos).

**46 hechizos disponibles en el minuto 0** es, en la práctica, un menú de trucos: no hay
decisión de construcción, no hay coste de oportunidad, y como 46 tienen `manaCost: 0`, tampoco
hay economía dentro del combate.

## 6. Itemización y equipo — 1/10

`ItemDefinition` lleva la ficha completa de un arma: `damage`, `attackSpeed`, `range`,
`critChance`, `critMultiplier`, `durability`, `levelRequirement`, `weight`, `equipSlot`.
180 ítems autorizados, 15 equipables, 14 con daño real (de 2 a 18).

**Ningún sistema de combate lee ninguno de esos campos.** Los únicos lectores son:

- `ItemsRuntimeEditor` (el panel F7 los muestra y los edita)
- `DamageClassResolver`, que lee `EquipmentSlots` solo para `toolClass` / `toolTier` —
  es decir, para **talar árboles**, no para pelear.

`MeleeCombat` se inicializa una vez con `def.basicAttack` y nada lo vuelve a tocar. Equipar la
mejor espada del catálogo (daño 18) deja el golpe del jugador en 1 o 2.

`critChance` y `critMultiplier` no tienen implementación de crítico en ningún sitio del proyecto.

## 7. Combate cuerpo a cuerpo — 2/10

- Daño: `basicAttack`, 1 o 2, constante toda la partida.
- Cooldown y rango: literales `0.5f` y `1.5f` en el sitio de la llamada, iguales para las cinco clases.
- Defensa del jugador: 0. `Health.SetDefense` solo se llama en `ConfigureMonster`.
- Resistencias e inmunidades del jugador: vacías. `SetResistances` / `SetImmunities` solo en la ruta del monstruo.

Frente a hechizos de 22–45 de daño y coste 0, el melé no es una opción táctica: es una animación.

## 8. Recursos — 4/10

| Recurso | Estado |
|---|---|
| `Health` | ✅ completo — mitigación plana, resistencias, gracia post-golpe, `OnDamageBlocked` |
| `Mana` | ✅ completo — regeneración, bonus, consumo |
| `Energy` | ⚠️ clase escrita, **nunca se añade al jugador** — solo `ItemConsumer` la busca con `GetComponent` |
| `Hunger` | ⚠️ idéntico caso |

`Health` es la pieza mejor construida de toda la capa (y su `OnDamageBlocked` es una costura
correcta que ya usa el escudo esférico). Baja la nota que dos de los cuatro recursos existan
solo para que los consumibles que los rellenan no fallen.

## 9. Consumibles y buffs — 3/10

Curación, maná, energía y hambre se aplican de verdad en `ItemConsumer`. Pero el buff temporal
—`buffStat` / `buffValue` / `duration`, autorizado en el catálogo— es esto entero:

```csharp
Debug.Log($"[ItemConsumer] Buff +{value} to '{stat}' for {duration}s on {gameObject.name}");
// TODO: integrate with a StatComponent when implemented.
yield return new WaitForSeconds(duration);
```

Un frasco de "+5 fuerza durante 30 s" escribe dos líneas en la consola. Es honesto (lleva su
TODO), pero es la sexta pieza de la ficha del personaje sin destino, y la razón es siempre la
misma: **no existe un componente de stats mutables donde aterrizar**.

## 10. Misiones — 1/10

`QuestManager` está completo: objetivos, progreso, entrega, y recompensas de XP, **puntos de
habilidad** e ítems. Pero:

- **0 assets `QuestDefinition`** en el proyecto.
- `def.skillPointReward` va a `player.GetComponent<LearnedSkills>()`, que es `null`.

O sea: el único consumidor externo del árbol de habilidades también está vacío.

## 11. Economía y recompensas — 2/10

Hay oro, loot ponderado, cinco `VendorConfigDefinition`, márgenes por grupo económico y una
UI de tienda. Lo que falta es el otro extremo del bucle: **nada de lo que se compra hace al
personaje más fuerte**, porque el equipo no se lee y los buffs no existen. El oro compra
consumibles de curación y poco más.

## 12. Persistencia de la progresión — 5/10

`GameStateCollector` guarda `hp`, `maxHp`, `mana`, `maxMana`, `experience`, `level`, zona e
inventario, con escritura atómica, checksum y 5 copias rotatorias. Es sólido.

No guarda habilidades aprendidas ni puntos — correcto hoy, porque no existen — pero
`LearnedSkills.ToSnapshot()/FromSnapshot()` ya está escrito esperando a que alguien lo llame.
Baja la nota que `maxHp` y `maxMana` se persistan como valores absolutos: en cuanto haya
crecimiento por nivel o por skills, una partida guardada y una recalculada pueden divergir sin
que nada falle.

## 13. Legibilidad para el jugador — 2/10

La ficha de personaje (`CharacterSheetController`) tiene dos pestañas:

- **SKILLS** → `SkillTreeHUD`, que sale en la primera línea porque no hay árbol. Panel vacío.
- **STATS** → `StatisticsHUD`, que muestra **estadísticas de perfil** (partidas totales, tiempo
  jugado, logros, top de monstruos matados, partidas recientes). Útil, pero no es la ficha del
  personaje.

**No existe ninguna pantalla que diga cuánto daño hace el jugador, cuánta defensa tiene, o qué
le ha dado subir de nivel.** Aunque hoy la respuesta sería "1–2, 0, y nada", esa ausencia es la
razón de que once sistemas inertes hayan podido convivir sin que nadie lo notara jugando.

## 14. Calidad del código — 7/10

Hay que decirlo claro: **el código de esta capa es bueno**. Doc-comments que explican el porqué
(no el qué), decisiones de diseño argumentadas (el `(kind, key, value)` plano frente a
herencia), costuras correctas (`ReapplyAll`, `OnDamageBlocked`, `SetCurve`), null-safety
consistente, eventos en lugar de acoplamiento directo. `Health` y `Experience` aguantarían en
cualquier proyecto.

No es 10 porque hay dos modelos de datos rivales sin tipo común, las claves de `StatBoost` son
strings sin enum, y el bug de unidades de Python (`dragDropRange: 128`) sobrevive aquí igual
que sobrevivió en `wallWidth`, el tótem, el vórtice y el cono.

## 15. Tests frente a la realidad — 3/10

Existen `LearnedSkillsTests`, `SkillEffectApplicatorTests`, `LevelUpSkillPointSystemTests`,
`QuestManagerTests`, `ExperienceCurveIntegrationTests`. Todos verdes. Todos construyen su propio
árbol, su propio jugador y su propio catálogo en memoria.

Es el mismo fallo que `SPAWNER_COORDINATE_SPACE_DRIFT` y el incidente del chat: **probar cada
mitad por separado prueba nada sobre la composición**. Falta la clase de test que ya salvó a
los otros dos sistemas — el que afirma sobre **los bytes enviados** y sobre **la composición
completa**:

- "el jugador que aparece en la escena real tiene un `LearnedSkills` con un árbol asignado"
- "existe al menos un `SkillTree` en el proyecto y todos sus `skillId` son únicos y no vacíos"
- "subir de nivel cambia al menos un número observable del jugador"

## 16. Simetría jugador ↔ monstruo — 2/10

| Capacidad | Monstruo | Jugador |
|---|---|---|
| Defensa aplicada | ✅ `SetDefense(scaled.defense)` | ❌ nunca |
| Resistencias elementales | ✅ `SetResistances` | ❌ nunca |
| Inmunidades a estados | ✅ `SetImmunities` | ❌ nunca |
| Escalado por nivel | ✅ `GetScaledStats()` (hp/defensa/daño) | ❌ no existe |
| Rango / cooldown melé autorizados | ✅ en `EntityStats` | ❌ literales `0.5f, 1.5f` |

El jugador es hoy **la entidad menos configurable del juego**. Un murciélago tiene una ficha
más rica que cualquiera de las cinco clases jugables.

---

## El patrón que une los 16 ejes

No son dieciséis problemas. Es **uno**, repetido:

> Cada sistema de progresión tiene su capa de datos escrita, su capa de runtime escrita, su
> UI escrita y sus tests escritos — y le falta **o los assets, o la única línea de cableado**
> que lo conecta con la partida real.

Y falta siempre en el mismo sitio: **no existe un componente de estadísticas mutables del
jugador**. Sin él, `StatBoost` solo puede tocar dos claves, el buff de la poción no tiene
dónde aterrizar, el arma no tiene a quién sumar su daño, y el nivel no tiene qué subir.

Ese componente es la pieza que falta, y explica once de los dieciséis ejes a la vez.

---

## Propuesta: cómo darle sentido a todo

### La pieza central — `PlayerStats`

Un único componente que sea **la respuesta a "cuánto vale X ahora mismo"**, compuesto por
capas que suman, igual que `SpriteTintStack` es el único dueño del color de un sprite:

```text
valor final = base (clase) + nivel + skills + equipo + buffs temporales
```

- Cada fuente escribe **su propia capa** y nunca el total, así que dos fuentes no pueden
  pisarse (el mismo error que `SpriteTintStack` arregló para los tintes).
- Quitar una capa (desequipar, expirar un buff) es exacto por construcción, sin recalcular
  desde una "base" que alguien pudo haber contaminado.
- Un solo evento `OnStatsChanged` que la ficha de personaje y el HUD escuchan.

Stats mínimos para que los once sistemas inertes tengan destino: `maxHp`, `maxMana`,
`manaRegen`, `moveSpeed`, `meleeDamage`, `meleeRange`, `meleeCooldown`, `defense`,
`spellPower`, `critChance`, `critMultiplier`, `cooldownReduction`.

Deben ser un **enum**, no strings: hoy `SkillEffect.key` es un `string` y una errata solo se
descubre por un warning en tiempo de ejecución.

### Unificar los dos modelos de datos

`PlayerDefinition` y `EntityStats` deben converger. Lo mínimo: que la clase autorice defensa,
rango melé, cooldown melé, resistencias e inmunidades, y que `ConfigurePlayerStats` los empuje
a `Health` exactamente igual que `ConfigureMonster` ya hace. Eso solo ya cierra el eje 16.

Y renombrar: `maxStrength` → `baseMaxHp`, `maxIntelligence` → `baseMaxMana`. Si se quieren
atributos de verdad (fuerza / inteligencia / destreza), que sean **entradas** que deriven HP,
maná y daño, no alias de los recursos.

### Los hechizos se aprenden

Un `SpellDefinition` gana `levelRequirement` y `unlockCost` (o queda tras un nodo del árbol vía
`SkillEffectKind.UnlockSpell`, que ya existe). `EntitySetup` deja de registrar los 77 y registra
solo los conocidos; el guardado persiste la lista. Los 46 hechizos con `manaCost: 0` necesitan
un pase de coste, o el maná nunca será una decisión.

### El equipo pesa

`MeleeCombat` deja de recibir un `int` congelado y consulta `PlayerStats`. El arma equipada
aporta su capa. Implementar el crítico (`critChance` / `critMultiplier` ya autorizados en 14
ítems). Eso convierte 180 ítems de decoración en progresión.

### El nivel da algo

Crear el asset `LevelStatCurve` (HP y maná por nivel) y el `XpCurveDefinition` (curva + tope).
Añadir `LearnedSkills` al jugador y crear un `SkillTree` por clase. Con eso, las tres ramas de
`OnLevelUp` dejan de salir en silencio el mismo día.

### La ficha dice la verdad

Una tercera pestaña **CHARACTER** que liste cada stat con su desglose por capas
("Daño melé 14 = 2 base + 4 nivel + 6 espada + 2 buff"). Es lo que impide que la próxima
funcionalidad inerte pase desapercibida un año.

---

## Roadmap por fases

Cada fase deja el juego jugable y verificable por sí sola.

### Fase 0 — Cerrar la asimetría (barata, alto impacto)

1. `ConfigurePlayerStats` llama a `Health.SetDefense`, `SetResistances`, `SetImmunities`.
2. `PlayerDefinition` gana `defense`, `meleeRange`, `meleeCooldown`, `resistances`, `statusImmunities`.
3. Borrar o marcar `[Obsolete]` los campos muertos (`initial*`, `dashCharges`, `dragDropRange`),
   o cablearlos — pero no dejarlos como están, que es lo que enseñó `EntityStats.spawnMargin`.

### Fase 1 — `PlayerStats` por capas

1. `StatKind` (enum) + `PlayerStats` (componente por capas) + `OnStatsChanged`.
2. `MeleeCombat`, `Mana`, `Health`, `PlayerController` leen de ahí.
3. Test de composición: cambiar una capa cambia el número que el combate usa de verdad.

### Fase 2 — Nivel con recompensa

1. Assets `LevelStatCurve` y `XpCurveDefinition` (con tope).
2. Cerrar los 6 monstruos con `xpReward: 0`.
3. Test: subir de nivel cambia al menos un número observable.

### Fase 3 — Árbol de habilidades real

1. `AddComponent<LearnedSkills>` en `ConfigurePlayerStats` + un `SkillTree` por clase.
2. `SkillEffectApplicator` despacha sobre `StatKind`, no sobre dos strings.
3. Persistir aprendidas + puntos en el guardado (los métodos ya existen).
4. `SkillTreeHUD` deja de abrir vacío.
5. Test de bytes: existe ≥1 árbol, ids únicos, prerrequisitos sin ciclos.

### Fase 4 — Hechizos y equipo como progresión

1. Desbloqueo de hechizos (nivel o nodo) + persistencia de conocidos.
2. Pase de `manaCost` sobre los 46 gratuitos.
3. `MeleeCombat` consulta el arma; implementar crítico.
4. Buffs de consumible aterrizan en una capa temporal de `PlayerStats` (adiós al `Debug.Log`).

### Fase 5 — Legibilidad y contenido

1. Pestaña CHARACTER con desglose por capas.
2. Primeros `QuestDefinition` reales (el sistema ya funciona).
3. Diferenciar de verdad las cinco clases: árboles distintos, hechizos distintos, stats base
   distintos más allá de HP/maná/velocidad.

---

## Criterios de aceptación

Ninguna fase se declara hecha sin:

1. Consola MCP de Unity limpia (`refresh_unity` + `read_console`), regla cardinal del proyecto.
2. Un test de **composición**, no de mitad — el juego real, no un fixture sintético.
3. Un test sobre **los datos enviados** (assets del proyecto), no solo sobre el código.
4. Un número medido antes y después, en el cuerpo del PR.

---

## Lo que se implementó

Fecha: 2026-09-04. Verificado en vivo sobre una sesión de Play real, no solo en tests.

### La pieza central

`PlayerStats` — el único dueño de todo número que describe al jugador, compuesto por siete
capas independientes (`Base`, `Level`, `Skill`, `Grimoire`, `Equipment`, `Buff`, `Aura`).
Cada fuente escribe **solo su capa** y nunca el total, que es la regla que `SpriteTintStack`
estableció para el color de un sprite y por el mismo motivo: quitar una espada tiene que
quitar el +6 de esa espada y nada más, aunque una poción y tres talentos hayan tocado el
mismo stat mientras estaba equipada.

Composición publicada y fija:

```text
final = clamp((base + Σ Flat) × (1 + Σ PercentAdd) × Π (1 + PercentMult))
```

`PercentAdd` se agrupa (diez nodos de +5 % dan +50 %); `PercentMult` es su propio factor y
se reserva para capstones. Mezclarlos en un solo cubo es el bug de balance clásico donde el
apilado aditivo tardío deja sin valor a cualquier otra fuente.

### Vocabulario cerrado

`StatKind` — 14 valores, **todos con consumidor**: `MaxHp`, `MaxMana`, `ManaRegen`,
`MoveSpeed`, `MeleeDamage`, `MeleeRange`, `MeleeCooldown`, `Defense`, `CritChance`,
`CritMultiplier`, `SpellPower`, `SpellCooldownReduction`, `ManaCostReduction`, `XpGain`.

`PlayerStatsWiringTests` recorre el enum contra `PlayerStats.Consumers.cs` y falla ante
cualquier valor que no llegue a un componente. Ese test **es** el objetivo: es lo que impide
que esta capa se convierta en la duodécima capa autorizada e inerte del proyecto.

### Dos árboles, dos monedas

| | Talentos | Grimorio |
|---|---|---|
| Asset | `SkillTree`, uno por clase | `SpellTree`, uno por escuela |
| Nodo | `SkillNode` — **con rangos** | `SpellNode` |
| Moneda | punto de habilidad (1/nivel) | punto arcano (1 cada 2 niveles) |
| Concede | modificadores de stats, auras pasivas | desbloquea un `SpellDefinition` + maestrías |
| Componente | `LearnedSkills` | `KnownSpells` |
| Pestaña | SKILLS | GRIMOIRE |

La separación es la decisión de diseño que sostiene todo lo demás. Un talento es un **número**
y es por clase; un hechizo es un **verbo** y lo comparten las cinco clases. Un solo árbol pone
"+5 % de daño melé" a competir con "desbloquea Lluvia de Meteoros", que no es una elección
real; y una copia por clase del grafo de hechizos serían cinco assets divergiendo en el primer
retoque. La identidad de clase en el grimorio es `SpellTree.classAffinities` más un recargo
fuera de afinidad: una tendencia, no un muro.

### Contenido generado

`Valkur > Progression > Seed Progression Content` — 95 assets de talentos + 53 de grimorio.

- **5 árboles de clase**, misma forma (3 raíces → 3 nodos de segundo nivel → 1 capstone),
  números distintos: enano el muro, bárbaro el martillo, elfo la hoja, mago el lanzador,
  valquiria la híbrida. Cuestan 30–34 puntos contra un tope de nivel 60, así que una partida
  **no puede** completar su propio árbol.
- **9 escuelas** que cubren los **46 hechizos lanzables por el jugador**, cada uno exactamente
  una vez: martial, pyromancy, cryomancy, storm, arcane, radiance, shadow, verdant, ki. Dos
  hechizos son innatos (`slash_regular`, `weapon_toggle`).
- `XpCurve` (base 100, exponente 1.55, **tope 60**) y `LevelStatCurve` (+8 HP, +4 maná,
  +0.5 daño melé, +0.25 defensa, +0.05 regen por nivel).

El seeder **crea y no sobrescribe** — el contrato "los valores por defecto son de creación,
lo autorizado gana" que ya usan `TilesetRulesetImporter` y el importador de personas. La
variante que sobrescribe es otro ítem de menú tras una confirmación. Ninguno usa
`Undo.RecordObject`, por el motivo que registra la nota de las plantillas de edificios.

### Lo que dejó de estar inerte

| Antes | Ahora |
|---|---|
| `basicArmor` sin lector | `Health.SetDefense` — medido en vivo: defensa 5 en el enano, antes 0 |
| resistencias e inmunidades solo en monstruos | el jugador las recibe por el mismo seam |
| 77 hechizos entregados en el frame 0 | libro sincronizado a lo conocido — medido: **77 → 2** en un enano nuevo |
| `damage`/`attackSpeed`/`critChance`/`critMultiplier` de ítems solo en el editor F7 | capa `Equipment`, con `CritResolver` implementando el crítico |
| buff de poción = un `Debug.Log` | `TimedBuffSource`, con clave por ítem: repetir refresca, no apila |
| subir de nivel no cambiaba nada | capa `Level` reconstruida desde el nivel actual |
| pestaña "STATS" = estadísticas de perfil | pestaña **CHARACTER** con desglose por capas; la de perfil pasa a llamarse RECORDS |
| `XpGain` no existía | multiplicador aplicado en el único seam por el que pasa toda concesión de XP |

### Decisiones que merecen quedar escritas

- **`ItemDefinition.range` NO se mapea, deliberadamente.** Sus valores publicados son 1, 2,
  5, 6 y 8 contra un alcance melé autorizado entre 0.6 y 3.0 unidades de mundo: está en otra
  unidad, casi con seguridad la escala Python que este proyecto ya ha cazado filtrándose
  **cinco veces** (`wallWidth`, el radio del tótem, el del vórtice, `coneLength`, el radio de
  `arcane_flame`). Una conversión adivinada sería la sexta. Hay un test que fija la omisión
  para que sea una decisión y no un descuido que alguien "arregle" luego.
- **`attackSpeed` se convierte como recíproco, no como negación.** El campo publicado es un
  multiplicador de ritmo (0.8 a 1.5, 1 = normal) y el store habla en segundos de cooldown:
  1.5 ataques por segundo es un cooldown de 1/1.5, es decir −33 %. Escribir −0.5 habría
  reducido el intervalo a la mitad.
- **`critMultiplier` aporta solo lo que excede de 1.** El campo reposa en 1 en el esquema;
  sumarlo crudo habría regalado +150 % de daño crítico a cada arma del catálogo por llevar su
  propio valor por defecto.
- **Un save vacío se trata como migración, no como dato.** Bug encontrado midiendo en vivo:
  cargar una partida antigua ponía **ambos** balances de puntos a 0 y destruía la concesión
  inicial. Ahora `RestoreFrom` detecta el documento vacío y reconstruye lo que los niveles
  ganados deberían haber pagado, que es la única migración que deja a un personaje legacy de
  nivel 30 capaz de abrir algún árbol.
- **El editor F4 levanta la restricción mientras está abierto** (`SetAuthoringUnlockAll` +
  re-registro del catálogo). Sin eso el editor podría seleccionar cualquier hechizo y lanzar
  solo los que el personaje conoce, y los diecinueve `AnimationProbe` serían inalcanzables.

### Verificación

- Consola de Unity limpia: 0 errores tras cada lote.
- Compilación confirmada por sonda de tipos vía `execute_code`, no solo por consola limpia
  — la regla de CLAUDE.md "una consola limpia no es una compilación correcta".
- Composición verificada **en vivo** sobre una sesión de Play real:
  `stats=True prog=True tree=dwarf schools=9 book=2 hp=200 def=5 melee=1`.
- Suites nuevas: `PlayerStatsTests`, `PlayerStatsWiringTests`,
  `ShippedProgressionContentTests`, `EquipmentAndBuffStatTests`, más `LearnedSkillsTests`
  reescrito para rangos, respec y el documento de guardado compartido.

### Lo que sigue abierto

1. **Fase 5 — contenido.** Sigue habiendo **0 assets `QuestDefinition`**; el sistema de
   misiones funciona y no tiene datos.
2. **`ItemDefinition.range`**: establecer qué unidad es y mapearlo, o borrarlo.
3. **Auras pasivas**: `AuraRegistry` no tiene API de eliminación, así que un aura de talento
   se aplica una vez y un respec no la retira. Los nodos publicados hoy no usan auras.
4. **`dashCharges`, `basicDeathTimerDuration`, `damageStopProbability`, `dragDropRange`,
   `maxDexterity`, `initial*`** siguen sin lector en `PlayerDefinition`. Se dejaron fuera del
   enum a propósito: añadir un `StatKind` sin consumidor es exactamente lo que el test de
   cableado existe para impedir.
5. **Crítico en hechizos**: `CritResolver` solo lo aplica al melé; el daño de hechizo escala
   con `SpellPower` pero no puede criticar todavía.
