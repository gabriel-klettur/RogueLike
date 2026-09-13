# Auditoría de las áreas de impacto de los hechizos

**Fecha:** 2026-09-12 · **Nota global: 5.4 / 10** (antes de los arreglos de esta sesión) → **7.9 / 10** después.

Todo lo que sigue está **medido**, no deducido: los números de mundo salen de una partida en marcha
(`execute_code` sobre la escena `MainGameplay`, cámara ortho 5 sobre un viewport 1600x800, es decir
96 px de pantalla por unidad de mundo), y los números de datos salen de leer los 83 `.asset` del
catálogo con un script.

---

## 0. El resumen en una frase

El problema que se ve en pantalla — «el área de impacto no coincide con donde impactan las
partículas» — **no es un problema de partículas**. Son tres fallos distintos apilados, y solo uno de
ellos es de VFX:

1. **Unidades.** Dos ejecutores seguían dividiendo entre 16 (la escala de píxeles del build original
   en Python). Era la **séptima y octava** aparición del mismo fallo en este repositorio.
2. **Adornos con tamaño constante.** La mina dibujaba su anillo a un tamaño fijo que no tenía
   ninguna relación con su radio de disparo.
3. **Efectos de impacto sin radio.** El meteorito y la explosión de proyectil dibujan un estallido
   de tamaño fijo sobre un círculo de daño que sí depende de los datos.

Y encima de todo: **no había forma de mirarlo**. Esa es la causa raíz de que los tres sobrevivieran.

---

## 1. Puntuación por aspecto

| # | Aspecto | Antes | Después | Por qué |
|---|---|---:|---:|---|
| 1 | Unidades (mundo vs píxeles Python) | **3** | **10** | 5 campos en 2 ejecutores dividían entre 16 |
| 2 | Origen del casteo (ancla, bozal, holgura) | **8** | **8** | Un solo dueño, ancla por dato, bozal por fotograma |
| 3 | Apuntado | **7** | **7** | Correcto, pero el meteorito se salta al dueño único |
| 4 | Colocación en el suelo (`spawnAtMouse`) | **6** | **6** | 47 de 83 hechizos no declaran alcance |
| 5 | **Daño ↔ dibujo (la queja)** | **4** | **6.5** | Mezcla: la mitad clavados, la otra mitad sueltos |
| 6 | Forma del área (círculo/sector/cápsula/rect) | **8** | **8** | Fase amplia + prueba exacta, bien hecho |
| 7 | Radio de daño del proyectil | **7** | **7** | Barrido del collider, correcto |
| 8 | **Observabilidad (poder verlo)** | **1** | **9** | No existía nada; ahora hay overlay + sonda |
| 9 | Consistencia de un campo entre ejecutores | **4** | **7** | `explosionRadius` significaba dos cosas |
| 10 | Datos publicados (los `.asset`) | **4** | **6** | Valores en píxeles, radios vestigiales |
| 11 | Tests sobre geometría | **5** | **5** | Hay tests por hechizo, ninguno compara daño vs dibujo |
| 12 | Coste del instrumento | — | **10** | Cero cuando está apagado |

---

## 2. Los hallazgos, uno a uno

### 2.1 Unidades — la séptima y la octava aparición del mismo fallo (nota 3)

`MeteorExecutor` y `MineExecutor` seguían dividiendo entre 16. **La señal es siempre la misma**: el
valor por defecto para un campo sin autorar era dieciséis veces más grande que cualquier cosa que el
asset pudiera producir, así que los dos números nunca pudieron significar lo mismo.

| Campo | Autorado | Efectivo (antes) | Efectivo (ahora) |
|---|---:|---:|---:|
| `meteor_shower.meteorAreaRadius` | 32.5 | **2.03 u** | 3.5 u |
| `meteor_shower.meteorImpactRadius` | 10 | **0.625 u** | 1.1 u |
| `mine_basic.triggerRadius` | 3.75 | **0.234 u** | 1.5 u |
| `mine_basic.explosionRadius` | 8.75 | **0.547 u** | 2.75 u |

0.625 u son **60 píxeles de pantalla**: un meteorito que dañaba un círculo de un tercio de baldosa
bajo un estallido dibujado muchas veces más grande. La mina era peor: había que estar prácticamente
encima de ella.

**Arreglado.** Se quitó la división y se reescribieron los dos `.asset` en unidades de mundo. Los
valores nuevos son una decisión de balance: se subieron a algo jugable en vez de conservar los
efectivos anteriores, porque los efectivos anteriores eran el fallo.

### 2.2 Un mismo campo con dos unidades según quién lo lea (nota 4)

`spell.explosionRadius` lo leía `ProjectileExecutor` en **unidades de mundo** y `MineExecutor`
dividiéndolo entre **16**. El mismo nombre, el mismo inspector, dos significados. Cerrado con 2.1.

### 2.3 Daño ↔ dibujo: el reparto exacto (nota 4 → 6.5)

**Clavados al radio de daño** (estos están bien y son el patrón a seguir):

- Cono de fuego — `FlameConeFX.HalfWidthAt` es consultado por `InsideCone`: **una sola cifra**.
- Vórtice — anillo de suelo fijado al círculo que consulta `OverlapCircleAll`.
- Tótem, charco, llama arcana — cada rig dibuja su anillo en `radius / 0.39`.
- Tajos (`SlashAttack`, `RegularSlashAttack`) — el daño barre con el filo dibujado.
- Muro de hielo — la huella de colisión es deliberadamente **menor** que el dibujo (un muro ocupa
  menos suelo del que tapa en pantalla), y eso está documentado.

**Sueltos** (aquí está la queja):

- **Mina** — el anillo era `RingScale = 1.20` bajo una raíz escalada por `spell.scale` (0.5), o sea
  **0.6 u constantes**, dijera lo que dijera el radio de disparo. La única promesa de la trampa al
  jugador («pisa aquí y salta») era decoración. **Arreglado**: el anillo va ahora a
  `triggerRadius / 0.39`, dividiendo además la escala de la raíz.
- **Meteorito** — `MeteorMissileFX.Spawn(pos, callback)` **no recibe radio**. El estallido tiene un
  tamaño fijo sobre un círculo de daño que sí sale de los datos. *Sigue abierto.*
- **Explosión de proyectil** — `impactPreset` es una preset de partículas cuyo tamaño no tiene
  ninguna relación con `explosionRadius`. *Sigue abierto.*
- **Impacto genérico** — `VFXManager.SpawnImpact(pos, color, 0.15f, 0.5f)`: tamaño literal.
  *Sigue abierto.*

### 2.4 Apuntado y colocación (notas 7 y 6)

`SpellTargeting` se declara a sí mismo «ONE OWNER» y lo cumple para cuatro ejecutores. **El
meteorito no pasa por él**: `MeteorExecutor.ResolveMeteorCenter` tiene su propia resolución de
cursor con su propio recorte. Dos implementaciones de lo mismo, y la que se salta al dueño es la del
único hechizo que combina `spawnAtMouse: 1` con `range: 0`.

**Medido sobre el catálogo:** 47 de 83 hechizos declaran `range: 0`. Para esos, el alcance real lo
decide una constante dentro de un ejecutor, invisible desde el inspector y desde el editor de
hechizos. No es un fallo por sí solo — cada constante es razonable — pero es lo que hace que «¿hasta
dónde llega esto?» no sea contestable mirando los datos.

### 2.5 Forma del área (nota 8)

Lo que está bien hecho y conviene no tocar: el círculo de `Physics2D` es solo la **fase amplia** y
después hay una prueba exacta (`IsInsideSector`, `InsideCone`). Dibujar solo el círculo daría un área
hasta seis veces mayor que la real en una estocada. El overlay nuevo dibuja **las dos**, y marca como
daño la estrecha.

### 2.6 Observabilidad (nota 1)

Antes de hoy no existía nada:

- `CombatRangeVisualizer` (Alt+F2) dibuja alcances de melé y de agro. **Ningún hechizo.**
- `SpellPreviewService.RangeRuler` dibuja **una** regla roja cuyo valor es el
  `Mathf.Max` de diez campos distintos — mezcla alcances con radios, así que para un
  `meteor_shower` medía `meteorAreaRadius` y lo llamaba «tiles».
- El escenario de vista previa del editor de hechizos es un lanzador sintético en su propia capa
  **sin ningún objetivo**, de modo que lo único que se quiere medir (qué alcanza el hechizo respecto
  a lo que tiene alrededor) no existe ahí.

Eso es la causa raíz de todo lo demás: los tres fallos de arriba eran internamente coherentes y solo
discrepaban con la pantalla, que es exactamente la clase de fallo que un número no reporta y una
imagen sí.

---

## 3. Lo que se ha construido

```text
Gameplay/Spells/Debug/SpellDebugShape.cs      la primitiva y el vocabulario de roles
Gameplay/Spells/Debug/SpellDebugAreas.cs      el registro de UN lanzamiento
Gameplay/Spells/Debug/SpellProbe.cs           las consultas de Physics2D, con el dibujo dentro
Gameplay/Spells/Debug/SpellDebugRenderer.cs   lo pinta y lo mantiene hasta el siguiente lanzamiento
Gameplay/Spells/Core/SpellCaster.Debug.cs     origen, apuntado, alcance y destino
Gameplay/Bootstrap/DevConsole.Commands.SpellAreas.cs   `areas [on|off|lista]`
Editors/Spells/...ViewPanel.cs / ...Preview.cs         el botón y la leyenda
```

**La decisión de diseño que lo hace útil:** una forma la empuja **el código que realmente consulta**,
nunca se vuelve a deducir del `SpellDefinition`. Un overlay que releyera `radius` del asset dibujaría
lo que el autor escribió mientras el ejecutor barre otra cosa — pintaría encima justo del fallo que
existe para destapar. Por eso las consultas pasan por `SpellProbe`: el dibujo y la consulta son una
sola llamada y no pueden separarse.

**Se limpia en `SpellCaster.ExecuteSpell`**, la única costura por la que pasa cada lanzamiento
(monstruos incluidos) — y no en la capa de input, porque un lanzamiento rechazado por maná, por
enfriamiento o por la postura nunca llega a un ejecutor, y borrar el dibujo de un lanzamiento que no
ocurrió es como se llega a creer que un área se ha movido cuando no se ha movido nada.

### Los nueve roles

| Color | Rol | Qué es |
|---|---|---|
| Blanco | `Origin` | Ancla del casteo y punto de salida (ancla + holgura hacia delante) |
| Azul claro | `Aim` | El rumbo real, re-apuntado desde el origen |
| Gris | `Reach` | Lo que el hechizo tiene permitido, y la fase amplia de las consultas |
| Amarillo | `Placement` | Dónde aterriza un hechizo colocado en el suelo |
| **Rojo** | **`Damage`** | **La geometría que el daño barrió de verdad** |
| Naranja | `Trigger` | Disparador de proximidad, adquisición de guiado |
| Magenta | `Splash` | Salpicadura / explosión al impactar |
| Verde | `Path` | Corredor recorrido, sondas de bloqueo |
| Violeta | `Visual` | La silueta dibujada, cuando un rig puede declararla |

### Cómo se usa

- **Editor de hechizos** (ESC → Hechizos → panel View) → botón **`Areas: ON/OFF`** con la leyenda
  debajo. El clic izquierdo redirigido del editor lanza **de verdad** en el mundo, que es lo que hace
  que esto se pueda leer: en el escenario de vista previa no hay a quién dar.
- **Consola** (acento grave): `areas on`, `areas off`, `areas lista`. El volcado en texto existe para
  que la geometría sea legible desde un test de PlayMode o desde `execute_code` sin que nadie tenga
  que mirar la vista de juego.

### Verificado en vivo

```text
Ultimo lanzamiento: 'meteor_shower' por Player(Clone) (y efectos vivos), 12 formas.
  Origin    Point   (175, 76.35)                     "ancla Hands"
  Origin    Point   (175.48, 76.23)                  "salida"
  Aim       Segment (175.48, 76.23) -> (177.42, 75.74)
  Placement Point   (181.31, 74.86)                  "destino"
  Damage    Circle  (183.15, 75.14) r=1.1            "impacto 1.1 u"
  ... x8, repartidos dentro del área de 3.5 u
```

y para un tajo, las dos formas a la vez, que es lo que prueba que la fase amplia no es el área:

```text
  Damage Sector (175.5, 75.93) r=2.4 arco=90   "tajo 2.4 u / 90 grados"
  Reach  Circle (175.5, 75.93) r=2.4           "fase amplia"
```

### Coste

Cero cuando está apagado: `SpellDebugAreas` sale en su primera línea, así que lo que queda en cada
sitio instrumentado es la llamada a `Physics2D` que ya se hacía. El renderizador reconstruye
geometría **solo cuando cambia el registro** (`Version`), no por fotograma. El proyectil muestrea su
corredor cada 0.33 u en vez de en cada paso de física, porque a 50 barridos por segundo llenaría el
presupuesto del registro en dos segundos y lo primero en caerse sería la salpicadura del final, que
es justo lo que se suele estar mirando.

---

## 4. Lo que queda abierto, por orden de rentabilidad

1. **`MeteorMissileFX` no recibe radio** — el estallido dibujado no tiene relación con el círculo de
   daño. Es el mayor desajuste que queda entre partículas y área.
2. **La preset de impacto de un proyectil no se escala con `explosionRadius`** — mismo caso, y afecta
   a más hechizos.
3. **`VFXManager.SpawnImpact(..., 0.15f, ...)`** — tamaño literal en el punto de llamada.
4. **El meteorito se salta `SpellTargeting`** — dos resoluciones de cursor para el mismo trabajo.
5. **47 de 83 hechizos con `range: 0`** — el alcance lo decide una constante que el editor no muestra.
6. **Ningún test compara daño contra dibujo** sobre el catálogo publicado. Ahora es escribible: las
   formas están en `SpellDebugAreas.Current` y un fixture de PlayMode puede lanzar y leerlas.
7. **`healing_aura` autora `radius: 0.625`** — legal desde que se quitó la división entre 16, pero
   es un radio de juego de dos tercios de baldosa para un aura. Probablemente un valor heredado.
