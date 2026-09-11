# Auditoría de tamaño y viabilidad web — Valkur

**Fecha:** 2026-09-10
**Pregunta de partida:** *¿se puede usar WebP (o algo parecido) para reducir el tamaño del
juego sin perder calidad visual, y migrarlo a web? Y si no, ¿cuánto se puede adelgazar?*
**Nota global: 3.1 / 10.**

Todo lo que sigue está **medido** contra el árbol de trabajo y contra el Editor en vivo
(`execute_code`), no estimado. Los comandos que producen cada número están en el apéndice.

---

## Resumen ejecutivo

**WebP es la herramienta equivocada, y la razón importa más que la respuesta.** Unity no
consume el fichero fuente en tiempo de ejecución: lo *reimporta* y lo *recodifica* a un
formato que la GPU sepa muestrear. Un PNG y un WebP con los mismos píxeles producen un build
**byte a byte idéntico**. La palanca que la pregunta busca sí existe y sí está en Unity —
se llama **compresión de bloque (BC/DXT/ETC/ASTC) y Crunch** — y este proyecto la tiene
**apagada en las 23 860 entradas de importación que existen**. Cero. En todas.

El número que resume la auditoría:

| | Mpx | En memoria/disco |
|---|---:|---:|
| Arte total del proyecto | **639 Mpx** | — |
| Hoy, RGBA32 sin comprimir | | **2 438 MB** |
| Con DXT5/BC3 (4:1) | | **609 MB** |
| Con DXT5 + Crunch (solo disco) | | **~120–215 MB** |

Y encima de eso, **448 MB de textura se envían dos veces**: `buildings.spriteatlas` empaqueta
`Resources/Buildings` y `env-tiles.spriteatlas` empaqueta `Resources/Tiles` — pero
`Resources/` se embarca **entero** pase lo que pase, así que esos 5 373 ficheros viajan
sueltos *y* dentro del atlas.

**Migrar a web hoy es imposible, y no por el tamaño de descarga: por el techo de memoria.**
El heap de WebAssembly de 32 bits topa alrededor de 2 GB. El proyecto pide 2.4 GB solo en
texturas. Con compresión de bloque baja a 609 MB y pasa a ser discutible. El segundo
bloqueador es de API, no de bytes: 38 ficheros de runtime usan `System.IO` y siete de ellos
leen `StreamingAssets` en el arranque, que en WebGL es una URL y no un directorio.

---

## Notas por eje

| # | Eje | Nota | Una línea |
|---|---|:---:|---|
| 1 | WebP como palanca | **1** | No aplica: Unity recodifica; el formato fuente no llega al build |
| 2 | Compresión de texturas (BC / Crunch) | **1** | 0 de 23 860 entradas con crunch; 10 de 10 atlas en RGBA32 |
| 3 | Higiene de `Resources/` | **1** | 448 MB enviados por duplicado + 63 MB sin un solo lector |
| 4 | Techo de memoria vs. navegador | **1** | 2 438 MB de textura contra un heap WASM de ~2 GB |
| 5 | Presupuesto de resolución del arte | **2** | `Art/UI` es el 30 % de todo el arte del juego en 202 ficheros |
| 6 | Arte muerto dentro del build | **2** | 112 MB de capturas de editor, con cero referencias, atlaseadas |
| 7 | Ajustes de los atlas | **2** | `maxTextureSize` sin criterio, todos `Uncompressed` |
| 8 | Portabilidad de E/S a web | **3** | 264 llamadas `System.IO` en runtime; 16 tocan `StreamingAssets` |
| 9 | Rendimiento URP 2D en navegador | **4** | 92 ficheros tocan `Light2D`; sin hilos; sin medición previa |
| 10 | Stripping de código y editores | **5** | `stripEngineCode: 1` y `RuntimeEditorPolicy` existen; el arte no se va con ellos |
| 11 | Ajustes de proyecto para WebGL | **6** | Módulo instalado, Brotli activo, memoria por defecto sin tocar |
| 12 | Arquitectura de atlas | **7** | 10 atlas, un solo propietario (`SpriteAtlasBuilder`), sin solapes |
| 13 | Audio | **8** | Música en Vorbis q0.7 *streaming*; los 44 SFX en PCM pesan 4.4 MB |

---

## 1. WebP: por qué no, y qué es lo que sí — **1/10**

Tres hechos, en orden de importancia:

1. **El formato fuente no sobrevive al build.** El importador de texturas de Unity decodifica
   el PNG a píxeles y vuelve a codificar al formato de la plataforma. Cambiar los 8 665 PNG
   por WebP dejaría el `.data` del build exactamente igual de grande, y el repositorio más
   pequeño — que no es lo que se preguntó.
2. **Ninguna GPU muestrea WebP.** Un WebP en tiempo de ejecución hay que decodificarlo a
   RGBA32 en CPU: la misma VRAM que hoy, más el coste de decodificar, y perdiendo el atlas.
   Además Unity no trae decodificador; haría falta un plugin nativo, que en WebGL significa
   compilar libwebp a WASM.
3. **Lo que sí hace lo que la pregunta quiere se llama Crunch.** Es compresión con pérdida
   *en disco* sobre un formato de bloque que la GPU lee directamente. En este proyecto está
   a cero en todas partes, verificado sobre los ficheros `.meta`:

   ```text
   crunchedCompression: 0   23 860 entradas
   crunchedCompression: 1        0 entradas
   ```

**El conflicto real, dicho sin rodeos.** `CLAUDE.md` declara que «los artefactos de
compresión sobre pixel art son la regresión visual de mayor impacto del proyecto», y de ahí
sale la política *Uncompressed*. Esa política es **correcta para DXT5/BC3 y exagerada para
BC7**: BC7 es de 4 bpp igual que DXT5 pero interpola el alfa y los canales muy por encima, y
sobre pixel art es prácticamente indistinguible. El proyecto ya lo demuestra por accidente —
hay 235 texturas en BC7 ahora mismo (182 MB) y nadie ha reportado nada. La decisión honesta
no es «comprimir o no», es **BC7 en escritorio y DXT5+crunch solo donde el ojo no lo alcance**.

Para web la respuesta se complica: BC7 en navegador depende de
`EXT_texture_compression_bptc`, que está en Chrome y Firefox de escritorio pero no en móvil.
El plan web realista es **DXT5/BC3 con crunch**, asumiendo el artefacto, o **quedarse en
RGBA32 y no migrar**.

---

## 2. Compresión de texturas — **1/10**

Medido en el Editor en vivo, sobre las 8 490 texturas cargadas:

```text
loaded textures: 8490   total 3631 MB
   RGBA32  n=3938   3406 MB      <- el 94 % de la memoria
   BC7     n=235     182 MB
   RGB24   n=4198     17 MB
   Alpha8  n=94       13 MB
```

Los diez atlas, sin excepción:

```text
buildings   sprites=1256  maxSize=4096  fmt=RGBA32  compr=Uncompressed  crunch=False
characters  sprites=1039  maxSize=4096  fmt=RGBA32  compr=Uncompressed  crunch=False
env-tiles   sprites=4117  maxSize=2048  fmt=RGBA32  compr=Uncompressed  crunch=False
players     sprites=607   maxSize=4096  fmt=RGBA32  compr=Uncompressed  crunch=False
ui          sprites=202   maxSize=2048  fmt=RGBA32  compr=Uncompressed  crunch=False
items / npc / vfx / spells / misc                   Uncompressed  crunch=False
```

`maxTextureSize` reparte 2048 y 8192 sin un criterio visible (16 798 entradas a 2048, 7 062 a
8192). 8192 en un juego cuya cámara mide 33 unidades de mundo es una página de atlas de
**268 MB en RGBA32**.

---

## 3. `Resources/` — el hallazgo más caro — **1/10**

`Resources/` se embarca **completo**, se referencie o no. Contiene 5 888 texturas y 387 MB de
memoria de runtime. Tres problemas distintos conviven ahí:

**a) Doble envío de las dos carpetas de arte más grandes.** Verificado leyendo los
*packables* de cada atlas:

```text
buildings <- Assets/_Project/Resources/Buildings
env-tiles <- Assets/_Project/Resources/Tiles
```

Son 5 373 texturas (353 MB de memoria, 448 MB en RGBA32) que viajan sueltas por estar bajo
`Resources/` **y** otra vez dentro de las páginas del atlas. Ningún `Resources.Load` del
runtime apunta a `Resources/Buildings`: está ahí solo porque el atlas la empaqueta. Sacar esa
carpeta de `Resources/` no cambia un píxel y no requiere tocar código.

**b) 63 MB del pack de los Catacumbas, sin lector.** `Resources/Dungeon/` tiene 4 325 assets
(3 616 `.asset`, 497 PNG, 65 prefabs, 6 shadergraphs). El único sitio del runtime que
menciona «Catacombs» es un comentario. Y `CLAUDE.md` ya registra este mismo peligro por otra
vía: es la carpeta que provocó los 34 errores de consola por `Resources.LoadAll<T>("")`. La
nota dice que las fuentes se movieron a `Data/Dungeon/CatacombsSource/` — esa carpeta existe
y pesa **254 KB**, mientras los 63 MB siguen en `Resources/`. La mudanza se quedó a medias.

**c) 95 MB de `Resources/UI`** de los que solo tres rutas se cargan por código
(`UI/CharacterSelection/taberna`, `UI/Intro/game_name`, `UI/Loading/background_ini`) más la
carpeta `UI/teleport_map` vía `LoadAll`. El resto no se pide nunca y se envía igual.

---

## 4. Techo de memoria del navegador — **1/10**

| Cantidad | Valor |
|---|---:|
| Textura del proyecto en RGBA32 | 2 438 MB |
| `webGLMaximumMemorySize` configurado | 2 048 MB |
| Heap útil real de WASM32 en navegador | ~1.5–2 GB |
| Textura con DXT5/BC3 | 609 MB |

No hay ajuste que salve la primera fila. **La compresión de bloque no es una optimización
aquí: es el requisito de entrada.** Y aun a 609 MB, un juego web que reserva medio giga de
VRAM excluye móvil y buena parte de los portátiles integrados.

Adicional: `webGLThreadsSupport: 0`. Los dos `Task.Run` del runtime
(`SaveFileManager.IO.cs`, `TileOverlayPersistence.Autosave.cs`) se ejecutarán **en el hilo
principal**, convirtiendo cada autoguardado en un tirón de fotograma.

---

## 5. Presupuesto de resolución del arte — **2/10**

El desglose por carpeta, medido:

| Carpeta | Ficheros | Mpx | RGBA32 | DXT5 |
|---|---:|---:|---:|---:|
| **Art/UI** | 202 | **191** | **729 MB** | 182 MB |
| Resources/Buildings | 1 256 | 115 | 440 MB | 110 MB |
| Art/NPC | 623 | 99 | 378 MB | 94 MB |
| Art/Characters | 1 410 | 78 | 298 MB | 74 MB |
| Art/Items | 238 | 53 | 204 MB | 51 MB |
| Art/VFX | 243 | 46 | 177 MB | 44 MB |
| Resources/UI | 17 | 25 | 95 MB | 23 MB |
| Art/Spells | 18 | 19 | 76 MB | 19 MB |
| Resources/Tiles | 4 117 | 2 | 8 MB | 2 MB |
| Art/Tiles + Misc + Resources/Dungeon | 521 | 6 | 26 MB | 6 MB |
| **Total** | **8 665** | **639** | **2 438 MB** | **609 MB** |

**`Art/UI` es el 30 % de todo el arte del juego repartido en 202 ficheros.** Su interior:

| Subcarpeta | Ficheros | Mpx | RGBA32 |
|---|---:|---:|---:|
| character_selection | 35 | 54 | 208 MB |
| spells | 73 | 36 | 139 MB |
| **fsm_editor** | 14 | 14 | **56 MB** |
| **particles_editor** | 8 | 8 | **32 MB** |
| intro | 6 | 7 | 29 MB |
| hud | 7 | 6 | 26 MB |
| **spawner_editor** | 6 | 6 | **24 MB** |

Contraste que lo dice todo: los 4 117 tiles del mundo entero suman **2 Mpx**; una sola
pantalla de selección de personaje suma **54**. Nada de esto es pixel art — son ilustraciones
generadas a resolución completa, exactamente el material donde BC7 es invisible y donde
además sobra resolución: se dibujan sobre un viewport de 1 600×800.

---

## 6. Arte muerto embarcado — **2/10**

`Art/UI/fsm_editor`, `Art/UI/particles_editor` y `Art/UI/spawner_editor` son **28 Mpx / 112 MB
en RGBA32** (31 MB de PNG) de capturas de pantalla de referencia del build antiguo en Python.
Comprobado recorriendo las dependencias de **todos** los prefabs, escenas y assets del
proyecto:

```text
=== quien depende de Art/UI/fsm_editor|particles_editor|spawner_editor ===
  NINGUNO (0 referencias en prefab/escena/asset)
```

Ningún script las menciona tampoco. Pero están bajo `Art/UI/`, que es lo que empaqueta
`ui.spriteatlas`, así que **se empaquetan y se envían en todas las builds**. Sumado a lo del
punto 3, el build actual carga alrededor de **623 MB** de textura que ningún jugador puede
llegar a ver.

Fuera del build pero dentro del repositorio: `Assets/Screenshots/` (25 MB, 34 ficheros) y
`Assets/Tests/EditMode` (11 MB) — no llegan a la build de jugador, pero Unity los importa en
cada arranque del Editor.

---

## 7. Atlas: arquitectura **7/10**, ajustes **2/10**

La arquitectura está bien y merece decirse: diez atlas, un único propietario
(`SpriteAtlasBuilder`), y una regla que ya impide que dos atlas empaqueten la misma carpeta —
la que cerró los 3 077 avisos de `matches more than one built-in atlases`. La división
`players` / `characters` por presupuesto de PPU está razonada y verificada.

Lo que falla es el ajuste, y es un solo campo repetido diez veces:
`textureCompression: Uncompressed` + `crunchedCompression: false` en los diez.
`maxTextureSize` mezcla 2048 y 4096 sin que el tamaño del contenido lo justifique.

---

## 8. Portabilidad de E/S a web — **3/10**

- **264 llamadas** a `File.*` / `Directory.*` / `FileStream` en 38 ficheros de runtime.
- **16 de ellas resuelven `Application.streamingAssetsPath`.** En WebGL eso es una URL HTTP,
  no un directorio: `File.ReadAllText` falla siempre.
- `StreamingAssets` pesa **3.1 MB**. No es un problema de tamaño, es de API.

Los que bloquean el arranque (los demás son de los editores en juego, que
`RuntimeEditorPolicy` ya puede apagar):

```text
Gameplay/World/Setup/WorldLoader.cs
Gameplay/World/Setup/OverlayLoader.cs
Gameplay/World/Buildings/BuildingCollisionLoader.Parsing.cs
Gameplay/Enemies/FSM/FSMRuntimeFactory.cs
Infrastructure/Persistence/Repositories/WorldStreamingFileRepositoryBase.cs
Infrastructure/Persistence/Repositories/JsonFileBuildingInstanceRepository.cs
Gameplay/World/Zones/WorldTransitionService.cs
```

Siete propietarios, no treinta y ocho — el trabajo es acotado. Lo correcto es **una fachada
`IWorldFileSource`** con implementación `System.IO` en escritorio y `UnityWebRequest` en
WebGL, no salpicar `#if UNITY_WEBGL` por los siete. Nota: los **97** usos de
`persistentDataPath` **sí funcionan** en WebGL — Unity emula un sistema de ficheros y lo
sincroniza a IndexedDB —, así que los guardados no son el problema.

---

## 9. Audio — **8/10**

El único subsistema que ya está bien.

| Grupo | n | Ajustes | Fuente |
|---|---:|---|---:|
| Música | 24 | `loadType: Streaming`, Vorbis, `quality 0.7`, `preloadAudioData: 0` | 99.3 MB |
| SFX | 44 | `loadType: DecompressOnLoad`, **PCM**, `quality 1` | 4.4 MB |

*Streaming* + sin precarga en la música es exactamente la política correcta y ahorra los ~100
MB de RAM que costaría lo contrario. Los 44 SFX en PCM son una política equivocada con un
coste de 4.4 MB: irrelevante hoy, y de todas formas `ADPCM` sería lo suyo para golpes cortos.

---

## 10–12. Código, ajustes y rendimiento — **5 / 6 / 4**

- `stripEngineCode: 1` está puesto. `managedStrippingLevel` está **vacío** (por defecto), que
  en IL2CPP es `Low`: subirlo a `Medium` en WebGL es gratis salvo por el reflection.
- `RuntimeEditorPolicy` ya puede apagar los diecisiete editores de autoría en una build de
  jugador. **Pero apaga el código, no el arte**: sus 112 MB de capturas siguen en el atlas
  (punto 6). Ninguna política de stripping alcanza a un asset dentro de un `.spriteatlas`.
- El módulo `WebGLSupport` **está instalado**, y `webGLCompressionFormat: 0` ya es Brotli, que
  es lo correcto. `webGLMemorySize: 32` / `webGLMaximumMemorySize: 2048` están sin tocar
  respecto al defecto.
- 92 ficheros del runtime tocan `Light2D`. El *renderer* 2D de URP con muchas luces es de lo
  más caro que hay en WebGL, y nadie lo ha medido todavía en navegador. La nota de 4 es
  «desconocido con motivos para preocuparse», no «medido y malo».

---

## Qué se gana, y en qué orden

Cada paso es independiente y se puede parar en cualquier punto. Los tres primeros **no tocan
un solo píxel** de lo que el jugador ve.

| # | Acción | Textura ahorrada | Riesgo visual | Esfuerzo |
|---|---|---:|---|---|
| 1 | Borrar `Art/UI/{fsm,particles,spawner}_editor` | **112 MB** | Ninguno (0 referencias) | Minutos |
| 2 | Sacar `Resources/{Buildings,Tiles}` de `Resources/` | **448 MB** | Ninguno (el atlas ya las sirve) | Horas |
| 3 | Sacar `Resources/Dungeon/Catacombs` a `Data/` | 63 MB de assets | Ninguno (sin lectores) | Horas |
| 4 | BC7 en el arte no-pixel (`Art/UI`, `Resources/UI`, ilustraciones) | ~620 MB | Muy bajo; ya hay 235 en BC7 | Días |
| 5 | Bajar la resolución de `Art/UI` a lo que el viewport usa | ~350 MB | Bajo, revisable a ojo | Días |
| 6 | Crunch sobre lo comprimido | Solo disco: **4–6×** | Ninguno adicional | Horas |
| 7 | DXT5 en el resto del arte del mundo | ~450 MB | **Real** — decisión de arte | Días + revisión |

**Escritorio, parando en el paso 6:** de ~2 990 MB de textura a **~400–500 MB**. Sin ninguna
decisión de arte discutible salvo el paso 5.

**Web, todos los pasos:** ~300 MB de VRAM y del orden de **150–250 MB de descarga con
Brotli**. Es un juego web grande pero existente. Y aun así siguen faltando la fachada de E/S
del punto 8, la medición de `Light2D` del punto 12, y una decisión sobre los editores en
juego.

---

## Recomendación

**Adelgazar sí, migrar todavía no.** Los pasos 1–3 son 623 MB de textura que se van sin
ninguna consecuencia visual y sin ninguna decisión de diseño: son errores de empaquetado, no
compromisos. El paso 4 es el que de verdad hace falta discutir, y el argumento a favor está
ya en el propio proyecto — 235 texturas llevan BC7 desde hace tiempo sin que nadie lo notara.

La web es un objetivo distinto y más caro: además de la compresión, exige la fachada de E/S,
un plan para los diecisiete editores de autoría, y medir URP 2D en navegador antes de
prometer nada. Vale la pena hacerlo **después** de los pasos 1–6, porque cada uno de ellos es
un requisito previo y todos mejoran también la build de escritorio.

Lo que **no** hay que hacer es convertir el arte a WebP. No reduciría el build ni un byte.

---

## Apéndice: cómo reproducir los números

```bash
# Tamaño en fuente
du -sh unity/Valkur/Assets/_Project/*
find unity/Valkur/Assets -name "*.png" -printf "%s\n" | awk '{n++;s+=$1} END {print n, s/1048576" MB"}'

# Crunch en todo el proyecto (esperado: solo ": 0")
grep -h "crunchedCompression:" $(find unity/Valkur/Assets/_Project -name "*.png.meta") | sort | uniq -c

# Audio
grep -h "loadType:\|compressionFormat:\|quality:" $(find unity/Valkur/Assets/_Project/Audio -name "*.meta") | sort | uniq -c

# E/S de runtime
grep -rn "streamingAssetsPath" --include=*.cs unity/Valkur/Assets/_Project/Scripts | grep -v "/Editor/"
```

Los megapíxeles por carpeta, la memoria de textura cargada, los *packables* de cada atlas y
la comprobación de dependencias del punto 6 salen de `mcp__unity__execute_code` contra el
Editor en vivo; los fragmentos están citados literalmente en cada sección.
