# Auditoría de espacio sin pérdida de calidad — Valkur

**Fecha:** 2026-09-10
**Encargo:** reducir al máximo el espacio que ocupa el juego **sin perder nada de calidad**.
**Nota global del estado actual: 3.4 / 10.**

Sustituye el eje web de `SIZE_AND_WEB_AUDIT_2026-09-10.md`; los hallazgos de empaquetado de
aquella siguen vigentes y se reordenan aquí bajo el criterio estricto de «cero pérdida».

Todo está **medido**: cabeceras IHDR de los 8 665 PNG, hashes MD5 de los 4 060 ficheros
mayores de 4 KB, y decodificación real de píxeles y consulta de importadores contra el Editor
en vivo. **Dos suposiciones mías cayeron durante la medición** y están anotadas donde tocan,
porque las dos habrían costado trabajo inútil.

---

## Cómo está organizada

La restricción «sin perder nada de calidad» parte la lista en dos, y la frontera es real:

- **Nivel A — idéntico bit a bit.** Los píxeles que llegan a la pantalla no cambian. No hay
  nada que decidir: son errores de empaquetado.
- **Nivel B — idéntico en pantalla, distinto en el fichero.** Bajar una textura a la
  resolución a la que de verdad se dibuja. Con la cámara actual el resultado en pantalla es
  igual o mejor (menos *aliasing* al minificar), pero el fichero pierde información y con ella
  la opción de acercar la cámara en el futuro. **Es una decisión, no un hecho, y va con el
  factor de sobremuestreo medido de cada carpeta para que se pueda tomar.**
- **Nivel C — rechazado por el encargo.** BC7, DXT, Crunch y el *tight packing*. Todos
  reducen mucho y todos pierden algo. Quedan documentados al final por si la restricción se
  relaja.

---

## Punto de partida, medido

| | |
|---|---:|
| PNG en `Assets/` | **8 665** |
| Píxeles de arte | **667.4 Mpx** |
| En disco (PNG comprimido) | **839 MB** |
| **Textura en el build, RGBA32** | **≈ 2 438 MB** |
| Audio | 103 MB fuente (ya correcto) |
| Repositorio completo en disco | **20 862 MB** |

> **Nadie ha construido nunca este proyecto.** No existe `Library/LastBuild.buildreport`, así
> que los 2 438 MB son la suma de los píxeles por su formato asignado, no una medición del
> `.data` final. Es una cota buena, pero el único número honesto sobre «cuánto ocupa el juego»
> sale de un build — y ese build también resuelve el punto A1.

Y el número que ordena toda la auditoría:

> **375 ficheros — el 4.3 % de los PNG — contienen 454.7 Mpx, el 68.1 % de todo el arte del
> proyecto.** Son las imágenes de 1024×1024 y mayores. En RGBA32 pesan 1 734 MB de los 2 438.

---

## Notas por eje

| # | Eje | Nota | Una línea |
|---|---|:---:|---|
| 1 | Resolución frente a la dibujada | **2** | 68 % del arte en 375 ficheros; ítems sobremuestreados ×10.7 |
| 2 | Higiene de `Resources/` | **3** | 63 MB sin un solo lector; el doble envío está sin verificar |
| 3 | Duplicados exactos | **2** | 50 conjuntos, 130 ficheros, 42.2 MB byte a byte |
| 4 | Arte muerto en el build | **2** | 112 MB de capturas de editor, 0 referencias, atlaseadas |
| 5 | Margen transparente | **3** | 32 % del lienzo es vacío en ítems, hechizos y props |
| 6 | Consistencia del pipeline de arte | **3** | Dos generaciones conviviendo: 288 px procesado y 1024 crudo |
| 7 | Disco de desarrollo | **4** | 20.8 GB, de los que 14 GB son `Library` regenerable |
| 8 | Dimensionado de edificios | **8** | **Correcto** — comprobado, no tocar |
| 9 | Canal alfa | **9** | **Ya correcto** — Unity asigna RGB24 solo |
| 10 | Audio | **8** | Música Vorbis q0.7 *streaming*; SFX en PCM pesan 4.4 MB |
| 11 | Arquitectura de atlas | **7** | 10 atlas, un propietario, sin solapes |

---

# NIVEL A — sin pérdida absoluta

Nada de esto cambia un solo píxel en pantalla.

## A1. `Resources/{Buildings,Tiles}` — **SIN VERIFICAR, y era mi titular** — **?/10**

Los dos atlas más grandes empaquetan carpetas que viven dentro de `Resources/`:

```text
buildings <- Assets/_Project/Resources/Buildings     1 256 sprites
env-tiles <- Assets/_Project/Resources/Tiles         4 117 sprites
```

`Resources/` se embarca entero, así que la lectura evidente es que esas 5 373 texturas viajan
sueltas **y** otra vez dentro de las páginas del atlas: 448 MB por duplicado. **Esa era mi
conclusión y no la puedo sostener.**

Dos pruebas apuntan a lo contrario:

1. **El propio código dice que el sprite ya resuelve al atlas.** `BuildingObject.Assembly.cs`
   carga con `Resources.Load<Sprite>(template.assetPath)` — un `Sprite`, no un `Texture2D` —
   y su comentario documenta una regresión pasada en estos términos: «`sourceSprite.texture`
   **es la página del atlas** cuando el sprite vive dentro de un SpriteAtlas».
2. **Unity documenta que descarta las texturas fuente** de los sprites empaquetados en un
   atlas marcado *Include in Build*, y los diez lo están (`bindAsDefault: 1`).

Si eso se cumple aquí, el doble envío **no existe** y el ahorro es cero. Lo que sí queda en
pie es una consecuencia menor: `Resources/` impide descartar lo que no se usa, y de las 1 474
plantillas de edificio solo **147** aparecen en el mundo enviado — aunque el `BuildingCatalog`
las referencia todas, así que probablemente viajarían igual.

**Esto no se puede resolver leyendo: se resuelve con un build.** La prueba decisiva es generar
uno y leer su `BuildReport`, que da el tamaño por asset:

```csharp
// tras un build, Library/LastBuild.buildreport
var r = UnityEditor.Build.Reporting.BuildReport /* .packedAssets[].contents[] */;
```

Comprobado: **no existe ningún build previo** en este proyecto
(`Library/LastBuild.buildreport` ausente), así que nadie ha medido nunca el tamaño real del
juego. Hasta que se haga, **este punto no cuenta en los totales de abajo.**

## A2. `Resources/Dungeon/Catacombs`: 63 MB sin un solo lector — **1/10**

4 325 assets (3 616 `.asset`, 497 PNG, 65 prefabs, 6 shadergraphs). El único sitio del runtime
que menciona «Catacombs» es un comentario. `CLAUDE.md` ya dice que las fuentes se movieron a
`Data/Dungeon/CatacombsSource/`: esa carpeta existe y pesa **254 KB** mientras los 63 MB
siguen en `Resources/`. La mudanza se quedó a medias.

Y no es solo peso: es la carpeta que provocó los 34 errores de consola por
`Resources.LoadAll<T>("")` que `CLAUDE.md` documenta.

**Ahorro: 63 MB de assets (≈9 MB de textura).**

## A3. 130 ficheros byte a byte idénticos — **2/10**

50 conjuntos, **42.2 MB** en disco. Y el patrón no es aleatorio:

```text
3.49 MB x2  Resources/Buildings/combat/combat_training_yard.png || .../combat/training.png
3.24 MB x2  Art/UI/base_hud_hp_mp_2.png                        || Art/UI/hud/base_hud_hp_mp_2.png
2.82 MB x2  Art/UI/intro/Intro_drwaft.png                      || Resources/UI/Intro/Intro_drwaft.png
2.53 MB x2  Art/UI/background_ini.png                          || Resources/UI/Loading/background_ini.png
2.35 MB x2  Art/UI/character_selection/character_selection_drwaft.png
                                                               || Resources/UI/CharacterSelection/...
```

**El grupo dominante es `Art/UI/X` clonado en `Resources/UI/X`**, y es el peor caso posible:
la copia de `Art/UI/` entra en `ui.spriteatlas`, la de `Resources/UI/` se envía suelta por
estar en `Resources/`. Cada una de esas ilustraciones se carga **dos veces en RGBA32**.
`Resources/UI` son 16 ficheros de 1536×1024 = **24.6 Mpx = 94 MB**, casi todos duplicados.

Hay que conservar la copia de `Resources/`, no la de `Art/UI/`: tres rutas se piden por código
(`UI/CharacterSelection/taberna`, `UI/Intro/game_name`, `UI/Loading/background_ini`) y
`TeleportMapBackgroundProvider` hace `LoadAll` sobre `UI/teleport_map`.

**Ahorro: 94 MB de textura + 42 MB de disco.**

## A4. 112 MB de capturas de editor muertas — **2/10**

`Art/UI/fsm_editor` (14 ficheros, 14 Mpx), `Art/UI/particles_editor` (8, 8 Mpx) y
`Art/UI/spawner_editor` (6, 6 Mpx): capturas de referencia del build antiguo en Python.
Recorriendo las dependencias de **todos** los prefabs, escenas y assets del proyecto:

```text
=== quien depende de Art/UI/fsm_editor|particles_editor|spawner_editor ===
  NINGUNO (0 referencias en prefab/escena/asset)
```

Ningún script las menciona. Pero están bajo `Art/UI/`, que es lo que empaqueta
`ui.spriteatlas`: **se empaquetan y se envían en todas las builds**. `RuntimeEditorPolicy`
no las alcanza — apaga el código de los editores, no su arte, y ninguna política de
*stripping* llega a un asset dentro de un `.spriteatlas`.

**Ahorro: 112 MB.**

## A5. El 32 % del lienzo es margen transparente — **3/10**

Los diez atlas tienen `enableTightPacking = False` y los **8 648 sprites son `FullRect`**, así
que cada página guarda el rectángulo completo, margen vacío incluido. Decodificando píxeles de
verdad, por carpeta:

| Carpeta | Muestra | Lienzo | Caja envolvente | Tinta | Recorte sin pérdida |
|---|---:|---:|---:|---:|---:|
| Art/Items | 42 | 45.6 Mpx | 67 % | 34 % | **32 % del área** |
| Art/Spells | 18 | 19.9 Mpx | 64 % | 28 % | **35 %** |
| Resources/Buildings | 83 | 90.5 Mpx | 67 % | 48 % | **32 %** |
| Art/VFX | 30 | 33.6 Mpx | 99 % | 96 % | 0 % — ya ajustado |

Ejemplos concretos, todos en lienzos de 1024×1024:

```text
exp_orb_4.png       bbox=23%  ink=7%
exp_orb_3.png       bbox=24%  ink=8%
mushrooms_deocation bbox=27%  ink=17%
iron_sword.png      bbox=46%  ink=21%
```

Recortar el lienzo a la caja envolvente y recolocar el *pivot* dibuja exactamente los mismos
píxeles.

**Dos avisos que hacen esto menos trivial de lo que parece, y los dos están ya en
`CLAUDE.md`:**

- **Nunca recortar un fotograma de animación a su propio alfa.** La nota existe literalmente
  («Trimming an animation frame tight to its own alpha breaks it»): la capa y la espada mueven
  la caja envolvente en cada fotograma, así que el ciclo tiembla y los pies se despegan del
  suelo. Vale para props e ítems estáticos, **no** para `Art/Characters` ni `Art/NPC`.
- **Recortar un edificio rompe su `splitRatio`.** `BuildingObject.Assembly` parte el sprite en
  base y copa por una *fracción de su altura*; cambiar la altura del lienzo mueve esa línea en
  las 1 474 plantillas y en las 324 instancias colocadas. Se puede, pero exige rederivar
  `splitRatio` en la misma pasada.

**Ahorro: 92 MB en ítems y hechizos (seguro) + 141 MB en edificios (exige rederivar
`splitRatio`).**

## A6. Disco de desarrollo: 20.8 GB — **4/10**

No afecta al juego que se distribuye, pero sí a la máquina:

| Carpeta | Tamaño | Qué es |
|---|---:|---|
| `unity/Valkur/Library` | **14 065 MB** | Caché de importación. **Regenerable**, ya ignorada por git |
| `unity/Udemy_Inspiration` | 1 495 MB | Referencia de solo lectura. Candidata a submódulo |
| `.git` | 1 458 MB | Historia. Los PNG grandes no se delta-comprimen |
| `staging` | 710 MB | Fuentes de los pipelines. Ya ignorada |
| `graphify-out` | 641 MB | Grafo derivado. **Regenerable**, ya ignorada |
| `Assets/Screenshots` | 25 MB | 34 capturas. Unity las importa en cada arranque |

`Library` sola es el 67 % del disco y se reconstruye con borrarla. Las tres regenerables suman
**14.7 GB**.

---

# NIVEL B — la resolución que de verdad se dibuja

Aquí está el 68 % del arte, y aquí hay que decidir.

## B1. El proyecto tiene dos generaciones de arte conviviendo — **2/10 y 3/10**

El histograma de dimensiones lo enseña sin ambigüedad:

| Carpeta | n | Mpx | Tamaños dominantes |
|---|---:|---:|---|
| Art/Items | 238 | 53.6 | 64×`160²`, 61×`288²`, **39×`1024²`** |
| Art/UI/spells | 73 | 36.6 | **30×`1024²`**, 27×`320²`, 16×`384²` |
| Art/VFX | 243 | 47.4 | 210×`256²`, **15×`1024²`, 3×`2048²`** |
| Art/NPC | 623 | 100.6 | **43×`1024²`**, 36×`263×242`, 32×`527×291` |
| Art/Characters | 1 410 | 78.4 | `329×295`, `288×258`, `242×268` — todo ajustado |

**Los 1024 viven en la raíz de las carpetas; los tamaños pequeños viven dentro de las
subcarpetas que producen los pipelines.** `Art/Items/*.png` está a 1024;
`Art/Items/cook/dishes/*.png` a 288. No hay ni un nombre repetido entre los dos grupos: los
1024 son la única copia de esos objetos, no restos de una conversión.

Y el estándar no me lo invento — lo declara el propio proyecto, en
`tools/atlas/wave10/build_cook_items.py`:

> «Art is written at NATIVE resolution and centred on a square canvas — no upscaling […]
> **288 is the widest cell either sheet produces**, rounded up. […] draws a **288 px icon into
> a 48 px inventory slot**.»

## B2. El sobremuestreo, medido objeto a objeto

Un ítem se dibuja a `TARGET_TILE_FOOTPRINT = 1f`, o sea **1 unidad de mundo = 96 px de
pantalla**, y en el inventario en un hueco de `slotSize = 64f`. Un fuente de 1024 px es por
tanto **×10.7 lineal, ×114 en píxeles**.

Los monstruos se pueden medir uno a uno, porque `scaleIdle` da su altura real:

| Monstruo | Sprite | `scaleIdle` | En pantalla | Sobremuestreo |
|---|---:|---:|---:|---:|
| `barbol_baby` | 1024 px | 0.05 | **76 px** | **×13.3** (×177 en píxeles) |
| `barbol`, `_gris`, `_cyan`, `_morado`, `_musgo`, `_oscuro`, `_amarillo_4` | 1024 px | 0.15 | 230 px | **×4.4** |
| `barbol_boss` | 1024 px | 0.35 | 537 px | ×1.9 |
| `barbol_brother_felipondor` | 1536 px | 0.35 | 806 px | ×1.9 |
| `barbol_gigante` | 1024 px | 0.50 | 768 px | ×1.3 — correcto |

**Esta tabla es el argumento y también su límite.** `barbol_gigante` justifica su 1024; siete
hermanos suyos usan la misma resolución para dibujarse a 230 px. Si comparten fichero PNG, hay
que separarlos antes de redimensionar; si no, se redimensionan por separado.

## B3. Objetivos propuestos

Regla: **nunca por debajo de 1.5× el tamaño dibujado**, y nunca por debajo del estándar que el
propio proyecto ya usa para esa clase de objeto.

| Grupo | Hoy | Se dibuja a | Objetivo | Ahorro |
|---|---|---:|---|---:|
| `Art/Items` raíz, 42 ficheros | 1024² | 96 px mundo / 64 px hueco | **288²** (estándar propio) | **161 MB** |
| `Art/UI/spells`, 30 ficheros | 1024² | icono de hechizo | **384²** (ya usado en la misma carpeta) | **103 MB** |
| `Art/VFX`, 18 ficheros | 1024²–2048² | efecto | **256²** (estándar de la carpeta) | **100 MB** |
| `Art/NPC`, los ×4.4 y peores | 1024² | 76–230 px | **384²** | **≈134 MB** |
| `Art/Spells`, 18 ficheros | 1024²–1536×1024 | pendiente de medir | — | ≈72 MB |
| **`Art/UI/character_selection`** | 1536×1024 | fondo a pantalla completa | **no tocar** | 0 |
| **`Resources/Buildings`** | hasta 1024² | ver abajo | **no tocar** | 0 |

**Ahorro del nivel B: ≈500–570 MB.**

## B4. Tres suposiciones mías que la medición tumbó

Las dejo escritas porque las tres parecían obviamente ciertas, y una de ellas era el titular
de esta auditoría.

**«Los 840 PNG sin canal alfa se pueden pasar a RGB24 y ahorrar 139 MB.»** Falso: Unity ya lo
hace solo. De las texturas de más de 0.2 Mpx, 105 están ya en RGB24 y solo **4** están en
RGBA32 teniendo la fuente sin alfa. El ahorro real es de **2 MB**, no de 139. Comprobado con
`TextureImporter.DoesSourceTextureHaveAlpha()` contra `Texture2D.format`.

**«Los edificios a 1024 están igual de sobremuestreados que los ítems.»** Falso, y por mucho.
`scale` en `buildings_instances.json` está en **píxeles de textura**, no en tiles: las 324
instancias colocadas se dibujan con una altura mediana de **230 px y p90 de 399 px** a PPU 32,
o sea entre 690 y 1 200 px de pantalla. Un fuente de 1024 px ahí está **en la proporción
correcta o incluso corto**. Reducir los edificios habría degradado visiblemente lo más grande
del mundo. **Eje 8 = 8/10: no tocar.**

**«`Resources/{Buildings,Tiles}` se envía por duplicado: 448 MB.»** Sin verificar, y era la
cifra más grande del informe. La contradicen el comentario del propio `BuildingObject` («la
textura del sprite **es la página del atlas**») y el comportamiento documentado de Unity al
descartar las texturas fuente de un atlas *Include in Build*. Detalle completo en A1. Solo un
build lo resuelve.

De ahí salen las dos formas honestas de trabajar aquí: **medir el sobremuestreo por objeto**
en vez de aplicar una regla por carpeta, y **construir el juego antes de optimizarlo**, porque
en un proyecto que nunca se ha construido cualquier cifra de tamaño es una estimación.

---

# NIVEL C — lo que reduce más y queda descartado

Por el encargo («sin perder **nada** de calidad»), no porque no funcione:

| Técnica | Reduciría | Por qué queda fuera |
|---|---:|---|
| BC7 (`CompressedHQ`) | ~1 200 MB | Con pérdida, aunque en pixel art sea casi invisible. El proyecto ya tiene 78 texturas grandes en BC7 sin que nadie lo haya notado |
| DXT5/BC3 | ~1 800 MB | Con pérdida, y visible en bordes duros con alfa |
| Crunch | ×4–6 solo en disco | Exige un formato de bloque debajo. Hoy está a **0 en las 23 860 entradas** del proyecto |
| `enableTightPacking` | ~30 % de página | Cambia la malla del sprite de quad a polígono. `CLAUDE.md` ya documenta que `SpriteMeshType.Tight` costó 20.15 ms por `Sprite.Create` sobre una página de atlas |

Si algún día la restricción se relaja, **BC7 sobre las ilustraciones que no son pixel art**
(`Art/UI`, `Resources/UI`) es la de mejor relación: ~620 MB con un riesgo que el propio
proyecto ya ha aceptado sin darse cuenta.

---

# Plan, en orden de ejecución

Cada paso es independiente y se puede parar en cualquier punto.

| # | Acción | Ahorro | Nivel | Esfuerzo |
|---|---|---:|:---:|---|
| 0 | **Generar un build y leer su `BuildReport`** | — mide | — | 1 hora |
| 1 | Borrar `Art/UI/{fsm,particles,spawner}_editor` | 112 MB | A | Minutos |
| 2 | Borrar las 16 copias de `Art/UI` duplicadas en `Resources/UI` | 94 MB | A | Minutos |
| 3 | Resolver los otros 34 conjuntos de duplicados | 42 MB disco | A | 1 hora |
| 4 | Sacar `Resources/Dungeon/Catacombs` a `Data/` | 63 MB | A | Horas |
| 5 | Recortar el margen transparente de ítems y hechizos | 92 MB | A | 1 día |
| 6 | Redimensionar los 375 ficheros ≥1024 **por sobremuestreo medido** | ≈500 MB | B | 2–3 días |
| 7 | Recortar edificios rederivando `splitRatio` | 141 MB | A* | 2 días |
| 8 | Borrar `Library` y `graphify-out` en la máquina | 14.7 GB disco | A | Minutos |
| ? | `Resources/{Buildings,Tiles}` — **solo si el paso 0 confirma el doble envío** | ¿448 MB? | A | Horas |

`A*` = sin pérdida en píxeles, pero toca datos que hay que migrar en la misma pasada.

## Resultado

| | Textura en el build |
|---|---:|
| Hoy | **≈ 2 438 MB** |
| Tras los pasos 1–5 (nivel A puro) | **≈ 2 173 MB** |
| Tras el paso 7 | **≈ 2 032 MB** |
| Tras el paso 6 (nivel B) | **≈ 1 500 MB** |

**Reducción del 38 % sin que cambie un píxel en pantalla**, y hasta un 48 % más si el paso 0
confirma el doble envío. Si algún día se acepta BC7 en las ilustraciones, desde ahí se baja a
unos 500 MB.

---

## Recomendación

**El paso 0 va primero y no es burocracia.** Este proyecto nunca se ha construido, así que
nadie sabe cuánto ocupa realmente ni qué se está enviando. Un build da el tamaño por asset y
de paso resuelve el único punto de esta auditoría que no pude cerrar leyendo — que resulta ser
el que yo tenía como titular. Sin él se optimiza contra una estimación.

**Después, 1, 2 y 4.** Son 269 MB, no son compromisos sino errores de empaquetado, y ninguno
requiere una decisión de arte ni tocar el pipeline.

El paso 6 es el que de verdad hay que discutir, y la forma de discutirlo es la tabla de B2:
no «bajemos todo a 384», sino «`barbol_baby` se dibuja a 76 px desde un fichero de 1024».
`barbol_gigante` está bien como está y hay que dejarlo.

Y **no tocar** lo que ya está bien: los edificios están correctamente dimensionados, el canal
alfa ya lo resuelve Unity, `Art/VFX` ya está ajustado al 99 % y el audio es el subsistema
mejor configurado del proyecto.

---

## Apéndice: cómo reproducir

```powershell
# Cabeceras IHDR de los 8 665 PNG (ojo: -shl sobre [byte] se queda en dominio byte —
# hay que castear a [int] o todas las dimensiones salen 0)
$w = ([int]$buf[16] -shl 24) -bor ([int]$buf[17] -shl 16) -bor ([int]$buf[18] -shl 8) -bor [int]$buf[19]

# Duplicados: agrupar por tamano primero, hashear solo los grupos con colision
Get-ChildItem $root -Recurse -File | Group-Object Length | Where-Object { $_.Count -gt 1 }
```

```bash
# Crunch en todo el proyecto (esperado: solo ": 0")
grep -h "crunchedCompression:" $(find unity/Valkur/Assets/_Project -name "*.png.meta") | sort | uniq -c

# El estandar de 288 px, declarado por el propio pipeline
sed -n '168,200p' tools/atlas/wave10/build_cook_items.py
```

La cobertura de tinta, las cajas envolventes, el formato asignado por Unity, el sobremuestreo
por monstruo y la comprobación de dependencias del punto A4 salen de `mcp__unity__execute_code`
contra el Editor en vivo; los fragmentos están citados literalmente en cada sección.
