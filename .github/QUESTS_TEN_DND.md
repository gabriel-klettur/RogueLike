# Diez misiones, y la capa que las hace correr

> Diseñadas e implementadas el 2026-09-08. Inspiradas en los arquetipos que D&D
> estandarizó — las alimañas del sótano, el recado del herrero, la manada con su alfa,
> el banquete, la ronda del correo, la cripta, la vigilia, el grimorio robado, el
> contrato del mercader y el dragón del final — y escritas contra los datos que este
> juego tiene realmente en disco.

## Por qué esto y no otra cosa

La auditoría del mismo día puntuó las misiones en **1.0 / 10**: `QuestDefinition`,
`QuestManager`, `IObjective` y `KillCountObjective` estaban escritos y probados, y
había **cero misiones enviadas**, **cero llamantes de producción de `StartQuest`**,
**cero instanciadores de `QuestLogHUD`** y ningún dador. Era la pieza de mayor
apalancamiento del proyecto: convierte los NPCs, la economía, el mundo y el botín en
un bucle.

Ahora hay diez misiones que se pueden aceptar en una conversación, que avanzan solas,
que se guardan y que pagan.

## Las diez

Repartidas a propósito en **longitud** y en **tipo de objetivo**: entre las diez usan
los nueve tipos que existen, así que el contenido también es la prueba de la capa.

| # | id | Nombre | Da | Nivel | Objetivos | Largo | Recompensa |
|---|---|---|---|---|---|---|---|
| 1 | `q_despensa_plaga` | Plaga en la despensa | Gatita | — | 1 | XS | 120 xp · 40 mon · 2 locro |
| 2 | `q_yunque_frio` | El yunque frío | Smith | — | 2 | XS | 180 xp · 80 mon · 2 lingotes |
| 3 | `q_manada_alfa` | La manada y su alfa | Pavel | 2 | 2 | S | 400 xp · 150 mon · 1 punto de talento |
| 4 | `q_banquete_siete_fuegos` | El banquete de los siete fuegos | Gatita | 3 | 4 | M | 500 xp · 200 mon · 1 paella |
| 5 | `q_cartas_caminos` | Cartas para los caminos | Abigail | 2 | 4 | M | 350 xp · 250 mon |
| 6 | `q_cripta_colina` | La cripta bajo la colina | Felipondor | 5 | 3 | L | 900 xp · 400 mon · 1 talento · 1 grimorio |
| 7 | `q_vigilia_altar` | Vigilia en el altar | Felipondor | 6 | 3 | M | 700 xp · 300 mon |
| 8 | `q_grimorio_robado` | El grimorio robado | Roberto | 4 | 3 | M | 650 xp · 200 mon · 2 puntos arcanos |
| 9 | `q_deuda_abigail` | La deuda de la casa Abigail | Abigail | 3 | 2 | S | 300 xp · 1 rubí en bruto |
| 10 | `q_dragon_fosa_roja` | El dragón de la Fosa Roja | Smith | 10 | 5 | XL | 5000 xp · 1500 mon · 3 talentos · 3 arcanos · 1 espada |

### 1. Plaga en la despensa — el arranque

El sótano con alimañas, que en D&D existe por una razón concreta: enseña los verbos
sin castigar al que todavía no los sabe. Un objetivo, un tipo, cinco minutos, sin
nivel mínimo, y la paga es comida — que es justo lo que le hace falta a alguien que
acaba de empezar.

`KillCount barbol_baby × 6` · entrega a Gatita.

### 2. El yunque frío — el recado, y la primera ENTREGA

Recolectar doce de mineral y seis de carbón. Ambos objetivos llevan
`consumeOnComplete`, que es lo que separa "recoger" de "entregar": sin esa marca el
jugador se queda el mineral **y** cobra.

`Collect iron_ore × 12` + `Collect coal_chunk × 6` · entrega a Smith.

### 3. La manada y su alfa — chusma primero, nombre después

El escalón clásico: diez enemigos corrientes y luego el que los manda. La estructura
enseña, sin decirlo, que un enemigo con nombre no es un enemigo más grande.

`KillCount barbol × 10` + `KillCount barbol_muscle × 1` · requiere la 1.

### 4. El banquete de los siete fuegos — tres oficios en una

Tres recetas distintas más un ingrediente que se entrega. Es la única misión que toca
la capa de cocina, y existe también porque `CraftObjective` no puede expresarse como
un `Collect` sobre el plato: quien cocina tres guisos y se los come **los ha
cocinado**, y un recuento de mochila diría cero.

`Craft locro` + `Craft empanadas_argentinas` + `Craft churros_con_chocolate` +
`Collect beef × 4`.

### 5. Cartas para los caminos — viajar y hablar, sin una sola espada

Tres firmas en tres personajes distintos y un desvío por el bosque. Es la única de las
diez que se puede terminar sin combate, y está ahí a propósito: un juego cuyas diez
misiones se resuelven matando enseña que solo hay un verbo.

`Talk Pavel` + `Talk Valeria` + `Talk Roberto` + `Reach Forest`.

### 6. La cripta bajo la colina — el descenso

Llegar, limpiar y subir algo de abajo. La primera que exige nivel de verdad (5) y la
primera que manda al jugador a una zona que no es la suya.

`Reach dungeon` + `KillCount barbol_oscuro × 8` + `Collect obsidian_chunk × 5`.

### 7. Vigilia en el altar — aguantar

No pide ganar: pide seguir de pie. Tres minutos en el bosque y quince enemigos por el
camino. Es la única que mide TIEMPO, y el objetivo `Survive` se detiene mientras el
jugador está muerto — un reloj de pared habría terminado la vigilia solo mientras el
jugador era un fantasma.

`Reach Forest` + `Survive 180 s` + `KillCount cualquiera × 15`.

### 8. El grimorio robado — practicar antes de poder

Diez bolas de fuego, tres magos oscuros y el libro de vuelta. Es la única que paga en
**puntos arcanos**, que es la moneda del grimorio: la recompensa temática de una misión
sobre un libro de hechizos es otro hechizo, no oro.

`CastSpell fireball × 10` + `KillCount dark_mague × 3` + `Collect spellbook_simple × 1`.

### 9. La deuda de la casa Abigail — la única que mira el bolsillo

Quinientas monedas **en la mano**, no ganadas alguna vez: `EarnCoins` lee el saldo
vivo y BAJA cuando el jugador gasta. Es una misión de ahorro, y quien se lo funda no
la termina. Paga en gema, no en moneda, porque una banquera que paga en efectivo por
demostrar que tienes efectivo no dice nada.

`EarnCoins 500` + `Collect gold_nugget × 3`.

### 10. El dragón de la Fosa Roja — la capstone

Cinco objetivos, tres misiones previas, nivel 10 para que te la ofrezcan y nivel 12
dentro de ella. Es el único sitio del juego donde `red_dragon` tiene una razón de ser,
y paga más que las otras nueve juntas.

`ReachLevel 12` + `KillCount barbol_gigante` + `Survive 240 s` +
`KillCount red_dragon` + `Collect magma_heart`.

## Los nueve tipos de objetivo

| Tipo | Cómo avanza | Por qué así |
|---|---|---|
| `KillCount` | evento `OnEntityDied` | filtra al jugador y por `monsterKey`; vacío = cualquiera |
| `Collect` | **sondeo** de la mochila | el evento de recogida lleva el NOMBRE VISIBLE, no el id, y no se dispara si el objeto llegó fabricado, comprado o de recompensa. Contar la mochila no puede fallar por origen — y puede BAJAR |
| `Reach` | evento `OnZoneChanged` + comprobación al empezar | el evento no se dispara para quien ya está en la zona cuando acepta |
| `Talk` | evento `OnNpcConversed` (nuevo) | una vez por conversación ABIERTA, no por mensaje |
| `Craft` | evento `OnItemCrafted` (nuevo) | fabricar es un ACTO; un recuento de mochila no lo ve |
| `CastSpell` | evento `OnSpellCast`, filtrado al jugador | ese evento lo disparan también los monstruos, y las 22 sondas `anim_*` |
| `Survive` | **sondeo**, acumulando `deltaTime` | se detiene mientras el jugador está muerto; morir no reinicia |
| `ReachLevel` | **sondeo** del nivel | quien ya tiene el nivel al aceptar nunca vuelve a subirlo |
| `EarnCoins` | **sondeo** del monedero | saldo vivo, no ganancias acumuladas |

Los sondeados los tickea `QuestManager` cada 0.25 s. Un tipo sin caso en
`BuildObjective` no falla: **suelta el objetivo en silencio**, lo que hace la misión
más FÁCIL. Por eso `QuestObjectiveKindTests` recorre el enum entero.

## Decisiones que cuestan explicarlas y ahorran un fallo silencioso

- **La entrega es GENERADA, nunca escrita a mano.** Una misión con `turnInPersonaId`
  recibe un `TalkObjective` extra al final, con una puerta que exige que todo lo
  anterior esté hecho. Escrito a mano se cumpliría en la **misma conversación que
  entrega la misión**, porque quien la da y quien la cierra suelen ser el mismo
  personaje.
- **`Quest` escuchaba a UN solo tipo de objetivo.** El agregador hacía duck-typing
  sobre `KillCountObjective` y volvía a comprobar la finalización desde ese manejador.
  Así que una misión cuyo último objetivo fuese cualquier otra cosa se completaba y
  `OnCompleted` **no se disparaba nunca**: se quedaba activa para siempre y no pagaba.
  Era invisible mientras KillCount era el único tipo que existía, que es exactamente
  por lo que sobrevivió. Ahora todos reportan por `ObjectiveBase.Progressed`.
- **`IObjective` NO se ha ampliado.** Es lo que los fixtures implementan con tres
  líneas; un miembro nuevo ahí rompe todos los stubs sin ganar nada. El agregador
  pregunta `is ObjectiveBase` y trata lo demás como un contador pasivo, que es justo
  lo que un stub es.
- **`StartQuest` es permisivo y `IsEligible` es la puerta.** La consola y los tests
  necesitan forzar una misión sin montar un personaje de nivel 12 con tres
  prerrequisitos cumplidos. Meter la comprobación dentro de `StartQuest` haría
  intestable "dame esa misión".
- **El restore siembra los contadores SIN disparar el evento de progreso.** Un restore
  que reportase progreso volvería a recorrer la ruta de finalización y **pagaría la
  recompensa por segunda vez**. La reflexión sobre
  `<Current>k__BackingField` que hacía el código anterior se ha retirado: estaba a una
  reescritura de auto-propiedad de restaurar silenciosamente nada.
- **`QuestSaveData` va aplanado.** `JsonUtility` no serializa un array dentado, así que
  los contadores van seguidos con una lista paralela de longitudes. El empaquetado
  vive en un único sitio (`QuestManager.Persistence.cs`) porque dos copias de un
  par empaquetar/desempaquetar es cómo una de ellas se desfasa en uno y una misión
  vuelve con el progreso de otra.
- **Un documento de guardado vacío restaura un registro vacío, sin avisar.** Todo save
  escrito antes de esta capa es exactamente eso, y describe a alguien que no ha
  aceptado nada. Sin bump de esquema y sin migración.
- **La caja de misiones vive en `Resources/Quests/`.** `QuestService` lo monta
  `AddComponent` el arranque, así que no hay hueco en el Inspector desde el que llenar
  un `[SerializeField]` — es literalmente el defecto que dejó `ChatSystem._catalog` a
  null durante toda la vida del proyecto. Las diez definiciones viven FUERA de
  `Resources`, referenciadas por la caja: la carpeta que se empaqueta entera lleva el
  índice, no el contenido.
- **La lista de `Resources` está justificada en el test.** `AssetConventionsTests`
  lleva la razón escrita junto a la excepción, igual que Chat, Death y Progression.

## Cómo se llega a una misión dentro del juego

1. El jugador habla con un personaje (E sobre él, o clic).
2. `ChatSystem.OpenChat` dispara `OnNpcConversed(personaId)` — **después** de que el
   saludo esté puesto, para que la frase de la misión caiga debajo del hola.
3. `QuestService` responde hablando por boca del personaje: primero lo que está
   esperando que le cuenten, si no, lo primero que tiene que ofrecer. **Una sola frase
   por conversación**: un personaje con cuatro cosas que decir abriría con un muro.
4. Aparece el botón **Misiones** en la columna izquierda del panel — condicional, como
   Comerciar y a diferencia de Diario, porque un botón permanentemente muerto le enseña
   al jugador que no hace nada.
5. La hoja lista primero **LISTAS PARA ENTREGAR** y después **DISPONIBLES**. Ese orden
   es el diseño: quien ha cruzado el mapa con un trabajo hecho quiere el botón que lo
   cierra, y enterrarlo bajo tres ofertas nuevas es como se queda una misión abierta.
6. **Entregar no completa la misión**: dispara el mismo `OnNpcConversed` que dispara la
   conversación, y el objetivo de entrega generado es quien se entera. Una sola ruta de
   finalización, se haya pulsado un botón o simplemente hablado.

Y desde la consola, que es lo que hace medible todo lo anterior:

```
quest                      el registro: qué llevo y cuánto
quest <id>                 esa misión, objetivo a objetivo
quest offers [persona]     qué podría aceptar ahora mismo, y de quién
quest start <id>           fuérzala (dice por qué no era elegible, y la da igual)
quest abandon <id>         suéltala
quests [all]               todo, con el catálogo si se pide
```

## Lo que quedó fuera, y por qué

- **`ancient_relic_mask` no lo suelta nada.** Es el único objeto del catálogo marcado
  como de misión y ninguna tabla de botín lo produce, así que ninguna de las diez lo
  usa. La cripta pide obsidiana en su lugar. Arreglarlo es una entrada en una tabla de
  botín, no una decisión de diseño.
- **Sin misiones repetibles, sin diarias, sin ramas.** `Quest` es AND puro y una misión
  es de un solo uso. Ramificar necesita OR-semantics en el agregador, que es una capa y
  no un campo.
- **Sin marcadores en el minimapa.** `MinimapMarker` existe y el objetivo sabe qué zona
  quiere; falta el puente. Es lo siguiente con mejor relación valor/coste.
- **Sin escolta ni protección de un NPC.** Necesita IA de seguimiento, que no existe.
- **La recompensa en objetos no comprueba que haya sitio.** `Inventory.AddItem` devuelve
  el sobrante y aquí se ignora: con la mochila llena, la recompensa se pierde. El patrón
  correcto ya existe en `CraftingService.TryCraft` (quitar, poner, revertir el resto).
- **`arcanePointReward` paga a `KnownSpells` directamente.** Es correcto hoy porque solo
  hay un escritor, pero si aparece un segundo habrá que hacer lo mismo que
  `SetInvincible` enseñó: guardar y restaurar, nunca escribir a pelo.

## Verificación

- Consola de Unity limpia — 0 errores, 0 avisos accionables.
- **EditMode: 8155 / 8155 en verde** (132.9 s), incluidos los 48 tests nuevos.
- `ShippedQuestDataTests` valida los DATOS ENVIADOS contra los catálogos reales: cada
  `monsterKey`, `itemId`, `recipeId`, `spellKey`, zona y `personaId` de las diez
  misiones resuelve contra algo que existe. Cada uno de esos campos es una cadena que
  nada valida al escribirla, y todos fallan igual: en silencio.
