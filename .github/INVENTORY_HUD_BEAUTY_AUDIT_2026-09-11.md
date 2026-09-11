# Auditoría de belleza y UI/UX del inventario

**Fecha:** 2026-09-11 · **Nota global: 2.1 / 10** · Objetivo: **≥ 8.5**

Alcance: la ventana que construye `InventoryUI` (`Gameplay/Inventory/InventoryUI.cs`,
`.UIBuild.cs`, `.Refresh.cs`, `.SlotInteraction.cs`) más `InventorySlotDragHandler`, y su
botón en `HUDIconBar`. Fuera de alcance: la lógica de `Inventory` (salvo donde la UI miente
sobre ella), la tienda (`VendorShopUI`) y el crafteo, que se citan por coherencia.

Método: lectura completa del código que la dibuja + medición en vivo en Play Mode (1600x800,
canvas a escala 1.0) + la captura adjunta por el usuario + una captura propia con el panel
abierto sobre el pueblo, donde se ven a la vez el panel del jugador (rehecho hoy), la barra de
hechizos, la bandeja de iconos y el reloj. Todo número de este documento está medido, no
estimado, salvo donde se dice.

Documento hermano: **`.github/HUD_VISUAL_LANGUAGE.md`** — la respuesta a "¿cómo conseguimos
que todo el HUD hable el mismo idioma?". Esta auditoría dice qué está mal en el inventario;
aquel dice qué idioma tiene que hablar él y todas las demás ventanas.

---

## 0. El diagnóstico en una frase

**El inventario no es una ventana del juego: es un panel de editor que el jugador puede
abrir.** Viste literalmente el tema del editor de tiles (`TileEditorTheme.PanelBg`,
`.HeaderBg`, `.Border`, más `TileEditorUIHelpers` para el resto), de modo que al lado del panel
del jugador —piedra, filigrana dorada, fuente de píxel, rejilla de texel— aparece un rectángulo
gris azulado translúcido con Arial, que además tapa el minimapa. Los dos se ven a la vez en la
misma captura y parecen de dos juegos distintos.

---

## 1. Medidas en vivo

| Qué | Medido | Por qué importa |
|---|---|---|
| Rect del panel | x[1284..1584] y[156..784], **300 x 628 px** de 800 de alto | Es el 78 % de la altura de la pantalla |
| Rect del minimapa | x[1384..1576] y[540..776] | **Queda tapado al 100 %**, dentro del panel, a `sortingOrder` 200 contra 105. El rastreador de misiones (debajo del minimapa, orden 40) también |
| Fondo del panel | `TileEditorTheme.PanelBg` = (0.08, 0.08, 0.10, **0.82**) | El anillo del minimapa y su etiqueta **se ven a través del panel**: es el círculo tenue detrás de la rejilla en la captura del usuario |
| Slot vacío vs panel | luminancia **0.133 contra 0.122–0.125** | Un 1 % de canal. Los huecos solo se leen por el `Outline` de 1 px. Es el mismo defecto que `INPUT_FREE` sobre `BG_SURFACE` que CLAUDE.md ya registra en el editor de Controles |
| Contraste de las etiquetas del equipo | `TEXT_MUTED` (0.42, 0.44, 0.50) sobre 0.133 a **9 px** → **3.2 : 1** | WCAG pide 4.5 : 1 para texto pequeño. La línea de ayuda de abajo está igual |
| Imágenes con sprite | **1 de 77** | Todo lo demás son rectángulos de color sin textura: no puede haber chaflán, bisel ni esquina |
| Componentes `Outline` | **37** | Cada `Outline` duplica la geometría cuatro veces para simular un borde de 1 px |
| Fuente | `LiberationSans SDF` (la de TMP por defecto) | El panel del jugador ya tiene fuente de píxel propia con contorno |
| Escala del canvas | `ScaleWithScreenSize` 1600x800, match 0.5 | A 1080p el factor es 1.27: nada del panel cae en rejilla de píxel |
| Iconos de ítem | 229 iconos, point filter, **1024², 288² y 160²** | Se pintan en 44 px: minificación de 4x a 23x con filtro de punto y sin mipmaps = aliasing. `HudTextureBaker.Icon` ya resuelve esto en el panel del jugador |
| Ítems con rareza > Común | **91 de 236** | La rareza no se muestra en ningún sitio |
| Ítems con peso / durabilidad / nivel mínimo | **171 / 15 / 12** | Ninguno de los tres campos se muestra |
| Ítems equipables | 15: Weapon 8, Helmet 2, Chest 1, Shield 3, Book 1 | Ver D2 y D3 |
| Sonido de abrir | `inv_open` en `AudioCatalog.asset` con clip real (`open_inventory.wav`) | **0 llamadas** en todo el código |

---

## 2. Puntuación por eje

| # | Eje | Nota | Evidencia |
|---|---|---|---|
| 1 | Composición / layout | 4 | El orden cabecera → equipo → bolsa → ayuda → oro es correcto. Pero el equipo es una rejilla 3x3 idéntica a la bolsa, centrada con dos columnas muertas a los lados, y la píldora de oro flota sola en el pie |
| 2 | Jerarquía visual | 3 | Celdas de equipo y celdas de bolsa: mismo tamaño (52 px), mismo fondo, mismo borde. Nada dice "esto es lo que llevas puesto" |
| 3 | Materialidad del marco | 2 | `Image` sin sprite al 82 % de opacidad con un `Outline` gris. Es un placeholder; el minimapa se transparenta |
| 4 | Materialidad de los slots | 2 | 1 % de contraste con el fondo, sin hueco, sin bisel, sin sombra interior |
| 5 | Paleta | 3 | Grises azulados del editor. El dorado del tema aparece solo en "Lvl" y en las cantidades. Ningún color de rareza |
| 6 | Tipografía | 3 | Liberation Sans a 9–16 px, 9 px en las etiquetas del equipo, sin contorno, sin fuente del juego |
| 7 | Idioma / textos | 2 | "INVENTORY", "L. Hand", "double-click use" en inglés dentro de un juego en español ("NIVEL", "GUERRA", misiones, chat). "Sin descripcion" sin tilde. Y las teclas mienten (D1) |
| 8 | Iconos de ítem | 4 | Hay arte bueno para casi todo; se estropea al minificarlo 23x con filtro de punto |
| 9 | Rareza | **0** | Cinco niveles en los datos, cero píxeles en pantalla |
| 10 | Paper doll / equipo | 2 | Nueve celdas genéricas con texto; cuatro nombran huecos que ningún ítem puede llenar (D3); las celdas aceptan cualquier cosa (D2) |
| 11 | Cabecera / retrato | 3 | Retrato = frame vivo del cuerpo entero, diminuto; no reusa el recorte de cabeza de `HudPortrait`. "Lvl 1 (18%)" en texto plano, con `Mathf.Max(1, …)` que contradice al medallón en el nivel 0 |
| 12 | Tooltip | 2 | Caja fija de 38 px dentro del panel, solo al hacer clic, texto recortado. Sin estadísticas, aunque `EquipmentStatSource.AppendItem` existe literalmente "para que un tooltip muestre lo que hará equiparlo". Sin rareza, precio, peso, durabilidad ni requisito |
| 13 | Hover / selección | 2 | `slotHoverColor` serializado y nunca leído: no hay hover. Selección = tinte dorado al 65 % que tapa el fondo del icono. Las celdas de equipo no se pueden inspeccionar (el tooltip ignora sus índices) |
| 14 | Drag & drop | 4 | Funciona entre bolsa y equipo y hacia el mundo. Pero el origen no se atenúa, dentro del panel no hay destino válido/inválido (solo el arrastre desde el mundo pinta el borde amarillo), no hay animación al soltar, soltar fuera tira la pila entera sin confirmar y no se pueden dividir pilas |
| 15 | Oro | 3 | Un cuadrado amarillo plano como "moneda"; el número salta sin contar |
| 16 | Capacidad / peso | 1 | Ni "12/25" ni peso, con 171 ítems que tienen peso |
| 17 | Categorías / orden / búsqueda | 1 | El comentario de la clase promete "tabs" (paridad con Python). No hay pestañas, ni ordenar, ni filtrar |
| 18 | Motion | 1 | Abrir y cerrar es `alpha` 0→1 en un frame. Nada se anima |
| 19 | Feedback de eventos | 1 | Recoger, equipar, consumir, soltar, bolsa llena, subir de nivel con el panel abierto: nada pasa dentro del panel |
| 20 | Partículas / VFX | **0** | Ninguna |
| 21 | Sonido | 1 | El sonido de abrir existe, está catalogado y nadie lo llama |
| 22 | Rejilla de píxel | 2 | Escala de canvas no entera; iconos pixel-art resampleados. El panel del jugador ya tiene `HudPixelScaleFor` |
| 23 | Posición y convivencia con el HUD | 1 | Tapa el minimapa entero y el rastreador; se abre arriba a la derecha mientras su botón está abajo a la derecha; si lo arrastras, la posición no se recuerda |
| 24 | Coherencia con el resto del HUD | 1 | Es uno de seis dialectos visuales distintos en pantalla a la vez (ver `HUD_VISUAL_LANGUAGE.md`, secciones 1 y 5.1) |
| 25 | Tema / tokens | 2 | Lee un tema de EDITOR mutable: tocar los colores del editor de tiles recolorea el inventario del jugador. Siete `new Color(` literales en el builder y cuatro colores serializados "legacy" que nada lee |
| 26 | Accesibilidad | 2 | Contraste por debajo de WCAG, texto de 9 px, sin navegación por teclado ni mando, sin redundancia de forma |
| 27 | Robustez en build | 2 | El icono de la bandeja se carga con `AssetDatabase` bajo `#if UNITY_EDITOR`: en un build del jugador el botón del inventario es un cuadrado gris (lo mismo el de hechizos y el de música) |
| 28 | Chrome de ventana | 3 | Minimizar y cerrar hacen exactamente lo mismo (`MinimizeToTray` = `SetVisible(false)`). Botón rojo de editor |
| 29 | Rendimiento | 6 | Correcto para 34 celdas. 37 `Outline` multiplican vértices, `RefreshAll` repinta todo en cada cambio y `Debug.Log` sin gatear en cada apertura |

**Media ponderada: 2.1.** Los ejes 3, 4, 9, 12, 19, 20 y 24 pesan doble porque son los que
separan "funciona" de "hermoso" (el mismo criterio que la auditoría del panel del jugador).

---

## 3. Defectos: arreglar antes de embellecer

Un inventario precioso que miente sigue estando roto. Estos van primero.

- **D1 — la ayuda enseña teclas falsas.** El pie dice `Tab/I close | Q drop`. En el asset:
  `Tab` es `ToggleStance`, `Q` es `SpellTeleport`, y soltar es **`Delete`**. Un jugador que
  sigue la instrucción cambia de postura y lanza un teletransporte. El comentario de
  `InventoryUI.Update` ("`q` is still the drop key") también está desfasado. La ayuda debe
  leer el binding vivo (`InputControlPaths` ya traduce ruta → etiqueta), como hacen hoy los
  slots del panel del jugador.
- **D2 — el equipo acepta cualquier cosa y las estadísticas se apilan.** `TryDepositInEquipmentSlot`
  y `MoveSlotByIndex` no miran `equipSlot`: una poción entra en "Helmet" y una espada en
  "Boots". Y `EquipmentStatSource` suma todo ítem con `equipSlot != None` que esté en
  cualquiera de las nueve celdas, así que **nueve `knight_longsword` dan +162 de daño cuerpo a
  cuerpo**. Es un exploit de balance y a la vez una mentira de interfaz: las etiquetas prometen
  un hueco tipado que no existe.
- **D3 — el vocabulario de la UI y el de los datos no coinciden.** La UI dibuja
  `L. Hand / Helmet / R. Hand / Arms / Chest / Gloves / Pants / Boots / Jewelry`; los datos
  hablan de `Weapon, Offhand, Shield, Book, Helmet, Chest, Boots, Ring, Amulet, Trinket,
  Accessory`. **Cuatro de las nueve celdas (Arms, Gloves, Pants, Jewelry) nombran huecos para
  los que no existe ni un ítem** en el catálogo. Una celda que nunca se puede llenar es UI
  inerte — la forma "authored-and-inert" que CLAUDE.md registra una docena de veces.
- **D4 — tapa el minimapa y el rastreador.** Anclado a (-16, -16) arriba a la derecha con 628 px
  de alto, invade entera la columna que `HudLayout` declara del minimapa y de las misiones. Y
  se abre lejos de su botón.
- **D5 — translucidez que deja ver lo de detrás.** 0.82 de alfa en un proyecto en espacio
  lineal: el anillo del minimapa y su texto se leen a través del panel (visible en la captura
  del usuario). La regla del panel del jugador —"todo lo que tapa algo es opaco"— no se aplicó
  aquí.
- **D6 — el botón de la bandeja desaparece en un build.** `LoadHUDSprite` usa
  `AssetDatabase` y devuelve `null` fuera del Editor; `HUDIconBar` pinta entonces un cuadrado
  gris. Afecta también a `SpellBarHUD` y `MusicPlayerHUD`. Solución: referencia desde un asset
  de estilo bajo `Resources/UI/` (el patrón de `PlayerHudStyle`).
- **D7 — `inv_open` catalogado y muerto.** Clip real, cero llamadas.
- **D8 — el tooltip ignora el equipo y las estadísticas.** `UpdateTooltip` solo mira índices
  `< Slots.Count`, así que inspeccionar lo que llevas puesto no muestra nada, y nunca llama a
  `EquipmentStatSource.AppendItem`, que existe precisamente para eso.
- **D9 — dos lecturas del mismo nivel que discrepan.** `Mathf.Max(1, Level)` enseña "Lvl 1"
  mientras el medallón del panel del jugador enseña el 0 real del arranque.
- **D10 — minimizar = cerrar.** Dos botones, un efecto.
- **D11 — acoplamiento con el tema del editor.** `TileEditorTheme` es estático y mutable (lo
  edita el panel de UX del editor de tiles); el inventario lo hereda. Además `columns`,
  `slotSize`, `padding`, `panelColor`, `slotColor`, `slotHoverColor` y `selectedColor` son
  campos serializados que nadie lee (hay un `#pragma warning disable` para callar al
  compilador).
- **D12 — `Debug.Log` sin gatear** en cada apertura y en cada cableado del jugador. Ruido de
  consola; debe ir por `VerboseLog`.

---

## 4. Dirección visual

Una frase: **el inventario es la ventana más íntima del jugador —su equipo, su botín, su
progreso— y debe hablar el idioma del panel del jugador (piedra, oro, fuente de píxel, rejilla
de texel, eventos que se ven y estados que se leen), no el del editor.**

Las tres reglas que ya rigen el HUD rehecho y aquí no se cumplen, más una propia del inventario:

1. **Rejilla de texel.** Todo en texels enteros, escala entera (`HudPixelScaleFor`).
2. **Un evento se ve, un estado se lee.** Nada brilla en reposo, tampoco un legendario.
3. **La forma lleva el significado; el color lo confirma.** La rareza se lee por ornamento
   (esquinas) además de por color; un hueco de equipo se lee por su silueta, no por un texto
   de 9 px.
4. **La interfaz no puede prometer lo que los datos no cumplen.** Cada hueco dibujado acepta
   exactamente lo que su silueta dice, y cada tecla escrita es la del binding vivo.

### Esqueleto propuesto

```text
┌═════ INVENTARIO ══════════════ ◆ ═══════════════ [–][×] ┐   marco de piedra, filigrana
│ ┌────────┐  DWARF                          ⑫ ← medallón  │   dorada, gema, remaches
│ │ cabeza │  ▔▔▔▔▔▔▔▔▔▔▔ XP ▔▔▔▔▔▔▔▔▔▔▔  (mismas piezas)  │
│ └────────┘  ⚔ 18   ✚ 200   ◆ 35   ↯ 1.2 s                │   estadísticas DERIVADAS
│ ╔═════════════════════════════════════════════════════╗ │
│ ║ [casco]            ╭──────╮            [amuleto]    ║ │   paper doll: silueta del
│ ║ [arma ]            │ pj   │            [mano izq]   ║ │   personaje (frame idle E)
│ ║ [torso]            │ idle │            [anillo ]    ║ │   entre dos columnas de
│ ║ [botas]            ╰──────╯            [abalorio]   ║ │   huecos TIPADOS
│ ╚═════════════════════════════════════════════════════╝ │
│ [Todo][Equipo][Consumibles][Materiales][Misión]   [⇅]   │   pestañas por ItemCategory
│ ▣▣▣▣▣                                                   │
│ ▣▣▣▣▣   rejilla 5x5, huecos hundidos, marco por rareza  │
│ ▣▣▣▣▣                                                   │
│ ▣▣▣▣▣                                                   │
│ ▣▣▣▣▣                                                   │
│ 12/25 ▰▰▰▰▰▱▱▱   peso 34/60           ◉ 25   ← moneda   │   pie: capacidad, peso, oro
└──────────────────────────────────────────────────────────┘
      + tarjeta de tooltip flotante junto al cursor (HudTooltip)
```

- **Huecos de equipo = los del dato.** Arma (`Weapon`), Mano izquierda (`Offhand`, `Shield`,
  `Book`), Cabeza (`Helmet`, `Head`), Torso (`Chest`, `Body`), Botas, Anillo, Amuleto,
  Abalorio (`Trinket`, `Accessory`). Ocho huecos que existen, en vez de nueve de los que cuatro
  no. Cada hueco vacío dibuja una **silueta de glifo** (casco, espada, escudo…) generada como
  los `StatusGlyphs`, no una palabra.
- **Silueta del personaje en el centro** con el mismo recorte/horneado de `HudPortrait` (frame
  idle ESTE, factor entero), rehorneada al cambiar de loadout. Es lo que convierte nueve
  cuadrados en un paper doll.
- **Estadísticas de cabecera derivadas** de `PlayerStats` (daño, vida, maná, recarga), nunca
  calculadas aparte.
- **Marco por rareza en cada slot**: Común sin ornamento, Poco común 1 esquina, Raro 2, Épico
  4, Legendario 4 + filete. Colores convencionales (gris, verde, azul, violeta, naranja), pero
  la **cuenta de esquinas** es la redundancia para daltónicos.
- **Moneda**: un sprite de moneda de verdad en la fuente de píxel grande, con la cifra que
  cuenta hacia el valor nuevo.
- **Posición**: anclado a la derecha **a la izquierda de la columna del minimapa**
  (`HudLayout.ScreenMargin + TopRightColumnWidth + StackGap` desde el borde), de modo que el
  minimapa y las misiones sigan visibles. Arrastrable, con geometría en PlayerPrefs
  (`valkur.inventory.*`), el mismo patrón que `QuestLogHUD` y `MusicPlayerHUD`.

---

## 5. UX de nivel profesional

Lo que separa un inventario funcional de uno que da gusto usar, en orden de impacto:

| Mecánica | Hoy | Propuesta |
|---|---|---|
| Inspeccionar | Clic → caja fija de 38 px dentro del panel | **Hover** → tarjeta flotante junto al cursor (`HudTooltip`), título en color de rareza, tipo, estadísticas, precio de venta, peso, durabilidad, requisito de nivel (rojo si no se cumple) |
| Comparar | Imposible | La tarjeta de un equipable muestra la **diferencia** con lo equipado en ese hueco: `+6 ⚔` en verde, `-2 ✚` en rojo, calculado con `EquipmentStatSource.AppendItem` para ambos |
| Equipar | Arrastrar a una celda cualquiera | Doble clic o **clic derecho** equipa en su hueco (o intercambia); arrastrar resalta en dorado los huecos válidos y en rojo los inválidos, y el inválido rechaza |
| Usar | Doble clic solo en consumibles | Clic derecho usa un consumible; doble clic se mantiene |
| Dividir pilas | Imposible | Shift + arrastrar = mitad; Ctrl + arrastrar = una |
| Soltar al mundo | Tira la pila entera sin preguntar | Pide cantidad para pilas; confirma para Raro o superior |
| Ordenar | No | Botón `⇅`: por categoría, luego rareza, luego nombre; estable |
| Filtrar | No | Pestañas por `ItemCategory` (ya derivada por `ItemCategoryUtil`); los slots que no casan se atenúan en vez de desaparecer, para que la posición de las cosas no salte |
| Novedad | No | Punto "nuevo" en los ítems recién recogidos hasta el primer hover; contador en el botón de la bandeja mientras la ventana está cerrada |
| Bolsa llena | Silencio | Borde rojo del panel + "Bolsa llena" en la fuente de píxel + sacudida de 1 texel |
| Teclado / mando | No | Flechas mueven un cursor de slot; `ConfirmPressed` usa, `CancelPressed` cierra, vía `InputCompat` |
| Geometría | Se pierde al cerrar | Posición recordada; `Escape` cierra la ventana antes de abrir el Editor General (`EscapeOwnership`, como hace el mapa del mundo) |

---

## 6. Partículas: dónde sí, dónde no, y cómo

Mismo sistema que el panel del jugador: **`HudMoteLayer`** (quads de píxel en un único
`Graphic`, material aditivo `Valkur/UI/HudFx`, posiciones en texel, testable con `Tick(dt)`).
**Nunca `ParticleSystem` dentro del canvas** — en overlay no se ordena con las `Image`.

### Dónde NO

- Nada en reposo. Ni polvo ambiental, ni un legendario que centellea eternamente en la bolsa.
  Un inventario que brilla todo el rato no puede decir "acabas de conseguir algo bueno".
- Nada sobre el texto del tooltip.
- Nada que dure más de 0.8 s, excepto la secuencia de un legendario (1.2 s).

### Dónde SÍ (todo disparado por evento)

| Evento | Fuente | Efecto | Motas |
|---|---|---|---|
| Abrir | `SetVisible(true)` | Sin partículas. Entrada de 120 ms: el panel sube 4 texels y aparece; `inv_open` suena | 0 |
| Ítem entra en la bolsa | `OnInventoryChanged` + diff de slots | El slot que lo recibió destella 0.15 s, "+N" flotante en la fuente de píxel, 4 motas en el color de rareza | 4 |
| Ítem Raro o Épico entra | ídem, rareza ≥ Rare | Anillo que se abre desde el slot + 10–16 motas en el color de rareza | 16 |
| Legendario entra | rareza = Legendary | Secuencia de 1.2 s: viñeta dorada del panel, anillo doble, 24 motas en fuente, el ornamento del marco se "enciende" una vez y queda fijo | 24 |
| Equipar | `MoveSlotByIndex` hacia un hueco de equipo | Estela de motas del slot origen al hueco destino; la silueta del paper doll destella en blanco (mismo lenguaje que `WeaponSwapFlashFX`); las cifras de estadísticas que cambian cuentan y flotan `+6 ⚔` | 10 |
| Desequipar | el inverso | Estela apagada hacia la bolsa; las cifras bajan en rojo | 6 |
| Consumir | `ItemConsumer.TryConsume` ok | Motas que suben desde el slot en el color del efecto (verde vida, azul maná); la cantidad baja con un "tic" | 6 |
| Oro cambia | `CurrencyWallet.OnCoinsChanged` | Destello en la moneda; si el cambio es ≥ 50, 8 chispas doradas; la cifra cuenta | 8 |
| Arrastrar | `BeginSlotDrag` | Sin partículas: el fantasma se levanta (sombra de 2 texels, escala entera), el origen se atenúa al 35 %, destinos válidos en dorado | 0 |
| Soltar en un slot | `EndSlotDrag` | Rebote de 1 texel y 3 motas de "polvo" | 3 |
| Soltar al mundo | `DropSlotToWorld` | El fantasma se encoge hacia el cursor; sonido | 0 |
| Rechazo (bolsa llena, hueco inválido) | refusal | Borde rojo + sacudida de 1 texel; sin motas (un error no se celebra) | 0 |

Presupuesto: ≤ 48 motas vivas en el peor caso, fijado por test igual que en `PlayerHudTests`.
Regla de oro heredada: **sobre material aditivo, alfa es cobertura y color es brillo** — una
chispa legendaria "más intensa" es color > 1, no alfa más alto.

---

## 7. Roadmap por fases (nota estimada tras cada una)

### Fase 0 — Verdad antes que belleza (2.1 → 3.5)

- D1: la ayuda lee el binding vivo; D2: huecos tipados (`EquipSlot` → hueco) en `Inventory`,
  con test de que dos armas no suman y de que una poción no entra en la cabeza; D3: ocho huecos
  del vocabulario de datos; D4: nueva posición por defecto a la izquierda de la columna del
  minimapa; D5: fondo opaco; D6: sprites de bandeja desde un asset de estilo; D7: `inv_open`
  tras `HasSfx`; D8: tooltip de equipo con `AppendItem`; D9: nivel real; D10: un solo botón o
  un minimizar que colapsa a la barra de título; D11: fuera `TileEditorTheme` y los campos
  inertes; D12: logs a `VerboseLog`. Todo el texto en español.

### Fase 1 — Un solo lenguaje (3.5 → 6.5)

- **Mover el kit de píxel a `Valkur.UIKit`** (ver `HUD_VISUAL_LANGUAGE.md`, sección 5.2): hoy
  `HudArt`, `HudPixelFont`, `HudRect`, `HudMoteLayer`, `HudTooltip` y `HudTextureBaker` viven
  en `Valkur.UI`, y `Valkur.Gameplay → Valkur.UI` está prohibido — **el inventario no puede
  usar el panel del jugador aunque quiera**. Esa es la razón estructural de que haya seis
  dialectos.
- `InventoryHudStyle.asset` bajo `Resources/UI/` (geometría en texels, colores de rareza,
  referencia a los sprites de bandeja), leyendo la paleta compartida.
- Marco de piedra + filigrana, slots hundidos del atlas, fuente de píxel, escala entera,
  iconos minificados en GPU, paper doll con silueta, marcos de rareza, cabecera con el mismo
  retrato y medallón.

### Fase 2 — UX profesional (6.5 → 7.8)

- Toda la tabla de la sección 5: hover, tarjeta flotante con comparación, clic derecho,
  dividir pilas, confirmar al soltar, ordenar, pestañas, capacidad y peso, marca de "nuevo",
  navegación por teclado, geometría recordada, `Escape`.

### Fase 3 — Feel y partículas (7.8 → 8.8)

- La tabla de la sección 6 sobre `HudMoteLayer`, más motion de apertura/cierre y el conteo de
  cifras. Sonidos por evento, todos tras `HasSfx`.

### Fase 4 — Pulido (8.8 → 9.2+)

- `VendorShopUI` y `CraftingPanelUI` reusan el mismo componente de slot y la misma tarjeta de
  tooltip: comprar, craftear e inventariar pasan a ser la misma superficie con distinto verbo.
- La silueta del paper doll lleva puesto lo equipado cuando exista arte de loadout por pieza.
- Marco por clase (cinco variantes del 9-slice, mismo esqueleto).

---

## 8. Qué NO hacer

- **No pintar encima del panel actual.** Cambiar colores de `TileEditorTheme` "para el
  inventario" recolorea a la vez los editores. La belleza aquí es un cambio de dialecto, no de
  tono.
- **No copiar un inventario de Diablo con fondo de pergamino 3D.** El juego es pixel-art a 16
  PPU con piedra oscura y oro ya establecidos en el panel del jugador y la bandeja.
- **No animar estados.** Ni legendarios que respiran, ni slots que pulsan.
- **No dos componentes de slot.** Si el inventario, la tienda y el crafteo acaban con tres
  implementaciones del hueco, una estará mal dentro de un mes.
- **No embellecer antes de D1 y D2.** Un panel precioso que te dice que pulses Tab para cerrar
  y que te deja llevar nueve espadas sigue estando roto.

---

## 9. Resultado de la reconstrucción (mismo día)

**Nota global: 8.8 / 10**, desde 2.1. Verificado en vivo (capturas en Play a 1600x800 en el
pueblo, con doce objetos de rarezas mezcladas) y con tests EditMode: `InventoryWindowTests`
(21), `InventoryEquipmentTests` (reescrito para los huecos tipados), `InventoryUnifiedIndexTests`,
`InventoryFixedArrayTests` y `HudDialectGuardTests` (3, sobre las ocho carpetas de superficie
de jugador).

### Qué se construyó

```text
Data/Items/EquipmentLayout.cs          8 huecos tipados + mapa EquipSlot → hueco
Data/UI/HudTheme.cs (+asset)           tokens compartidos + rampa de rareza
Data/UI/InventoryHudStyle.cs (+asset)  texels, tiempos, motas, sonidos, icono de bandeja
Gameplay/Inventory/Inventory.Equipment.cs   reglas del equipo, equipar/quitar, restauración
Gameplay/Inventory/Inventory.Organize.cs    dividir pilas, ordenar (estable)
Gameplay/Inventory/InventoryUI(.Build/.Window/.Refresh/.SlotInteraction/.Feel).cs
Gameplay/Inventory/UI/                 InventoryArt, InventorySlotView, InventoryItemCard,
                                       InventoryConfirm, InventoryAudio, InventoryPointerRelay
Gameplay/UIKit/Hud/                    el kit del panel del jugador, movido para poder usarlo
Core/UI/HudLayout.cs                   MusicPanelWidth + GameWindowRightInset (banda R13)
```

### Defectos de la sección 3, estado

| # | Defecto | Estado |
|---|---|---|
| D1 | Ayuda con teclas falsas | Borrada; los gestos se enseñan en la tarjeta y la tecla de cerrar sale del binding vivo. Guard en `HudDialectGuardTests` |
| D2 | Equipo sin tipo, estadísticas apiladas | Huecos tipados en el MODELO (también el camino de soltar desde el mundo); nueve espadas → una |
| D3 | Vocabulario UI ≠ datos | Ocho huecos del vocabulario de `EquipSlot`, alias colapsados; test que recorre el enum |
| D4 | Tapaba minimapa y rastreador | Abre a la izquierda de la placa de música (`GameWindowRightInset`); arrastrable, recordado |
| D5 | Translúcido | Piedra opaca horneada |
| D6 | Botón de bandeja gris en build | Sprite desde `InventoryHudStyle.trayIcon` |
| D7 | `inv_open` muerto | Suena; el resto de eventos con sonido sintetizado tras `HasSfx` |
| D8 | Tooltip sin equipo ni estadísticas | Tarjeta flotante con estadísticas, comparación, efectos, valor, peso, durabilidad y nivel |
| D9 | Nivel que discrepaba | Medallón con el nivel real, el mismo que el panel del jugador |
| D10 | Minimizar = cerrar | Recoger deja la barra de título; cerrar cierra |
| D11 | Tema de editor y campos inertes | Fuera; `HudTheme` + estilo propio |
| D12 | `Debug.Log` sin gatear | Fuera (VerboseLog) |

Encontrados durante la reconstrucción, todos arreglados:

- **`iron_sword.asset` apuntaba al sprite de `ancient_relic_mask`**: la espada equipada se
  dibujaba como una máscara. Barrido del catálogo: era el único.
- **`StatModifier.Describe` mentía en dos sitios**, y es el formateador que comparten el árbol
  de talentos, la hoja de personaje y ahora la tarjeta: una recarga −16,67 % salía como
  "-16.67% Attack Speed" (un buff con signo menos) y la probabilidad de crítico +0,1 como "+0.1".
  Ahora la recarga se lee como velocidad (+20 %) y las fracciones como porcentaje. Y
  `StatCatalog` habla español.
- **Cargar una partida marcaba toda la mochila como nueva** (la restauración hace `Initialize` y
  un `SetSlot` por hueco, y cada uno parecía una recogida). Un vaciado completo silencia ese frame.
- **Las cifras de cabecera y pie se pisaban** ("200" encima de la gota de maná, el peso encima
  de la moneda) con columnas fijas; ahora fluyen por su tinta medida.
- **El filete legendario se leía como selección** (era un brillo); ahora es una línea de un texel.
- **El divisor bajo el título era una barra dorada de dos texels**: un 9-slice estira su columna
  CENTRAL, y el rombo del divisor estaba ahí. Ahora la línea grabada se estira y el rombo es una
  pieza aparte, centrada.
- **El inventario seguía tapando la esquina de la placa de música** con la primera banda (solo
  esquivaba la columna del minimapa). La banda `GameWindowRightInset` se deriva ahora del
  instrumento MÁS ANCHO de la derecha, `HudLayout.MusicPanelWidth`, que declara su dueño.

### Cómo se verificó

- **Capturas en Play** con `ScreenCapture.CaptureScreenshot` en la misma llamada que provoca el
  estado, con el componente desactivado para congelarlo: ventana abierta con doce objetos, tarjeta
  de comparación (bastón contra espada), rechazo por nivel ("NIVEL 5"), equipar con estela,
  arrastre (arma en dorado, botas en rojo, origen atenuado) y la pregunta de tirar un legendario.
- **La persistencia funcionó sola**: al volver a Play la ventana apareció recogida y movida
  donde alguien la había dejado en otra sesión; se desplegó para capturar y se restauraron
  esas preferencias después.
- **Tests**: 487/487 en `Game.HUD` + `Game.Player` + `DomainReloadStaticResetTests`. Una tanda
  intermedia dio cinco fallos de `ManaRegenAura*` que resultaron ser otra sesión entrando en Play
  a mitad de la tanda (con Play activo existe un `VFXManager` y esos tests asumen que no); la
  repetición limpia pasó entera.

### Puntuación por eje, después

| # | Eje | Antes | Después | Por qué no es 10 |
|---|---|---|---|---|
| 1 | Composición | 4 | 8.5 | Ventana alta (612 px a 1600x800); a 1280x720 queda justa |
| 2 | Jerarquía | 3 | 9 | El paper doll con marco dorado manda; bolsa y equipo ya no son iguales |
| 3 | Marco | 2 | 9 | La misma piedra horneada, filigrana, gema y remaches del panel del jugador |
| 4 | Slots | 2 | 9 | Hueco con bisel y sombra, opaco |
| 5 | Paleta | 3 | 9 | `HudTheme`; la rareza solo significa rareza |
| 6 | Tipografía | 3 | 8 | Fuente de píxel para cifras y títulos; TMP para nombres y tarjeta (acentos) |
| 7 | Idioma | 2 | 9.5 | Todo en español, ninguna tecla literal |
| 8 | Iconos | 4 | 9 | Minificados en GPU al tamaño exacto del hueco |
| 9 | Rareza | 0 | 9 | Esquinas 0/1/2/4/4 + filete legendario + color del título de la tarjeta |
| 10 | Paper doll | 2 | 9 | Ocho huecos tipados con silueta, figura del personaje en el centro |
| 11 | Cabecera | 3 | 8.5 | Nombre, medallión, XP con tooltip, cuatro cifras con glifo que cuentan al cambiar |
| 12 | Tarjeta | 2 | 9 | Estadísticas, diferencia con lo equipado, efectos, valor, peso, durabilidad, nivel, gestos |
| 13 | Hover / selección | 2 | 8.5 | Anillo de hover, oro de selección, "nuevo" hasta mirarlo |
| 14 | Drag & drop | 4 | 8.5 | Fantasma con cantidad, origen atenuado, destino válido/inválido, dividir, confirmar valiosos |
| 15 | Oro | 3 | 9 | Moneda pintada, cifra que cuenta, destello y chispas si la cantidad es grande |
| 16 | Capacidad / peso | 1 | 8 | "12/25" que se pone ámbar y rojo; peso total (el juego no tiene límite de carga) |
| 17 | Pestañas / orden | 1 | 8.5 | Seis filtros que atenúan en vez de esconder; ordenar estable y junta pilas. Sin búsqueda |
| 18 | Motion | 1 | 8 | Apertura con subida de 4 texels, cierre, contadores, estelas; todo en texels |
| 19 | Feedback de eventos | 1 | 9 | Recoger, equipar, quitar, consumir, soltar, ordenar, oro, rechazo |
| 20 | Partículas | 0 | 9 | Solo por evento, color de rareza, presupuesto por test, cero en reposo |
| 21 | Sonido | 1 | 7.5 | Sonidos sintetizados: correctos y discretos, pero no son arte grabado |
| 22 | Rejilla de píxel | 2 | 9.5 | Test que recorre +150 rects; iconos y TMP excluidos por diseño |
| 23 | Posición | 1 | 9 | Banda declarada, arrastre, recoger, recordado |
| 24 | Coherencia con el HUD | 1 | 9 | Mismo kit, misma piedra, mismo grid que el panel del jugador y la barra |
| 25 | Tema / tokens | 2 | 8.5 | Quedan ~8 literales de blanco/polvo en los efectos |
| 26 | Accesibilidad | 2 | 7 | Rareza por forma, contraste alto; sin navegación por teclado (las flechas mueven al personaje) |
| 27 | Robustez en build | 2 | 9 | Sin `AssetDatabase`; guard en todas las carpetas de jugador |
| 28 | Chrome | 3 | 9 | Arrastre, recoger ≠ cerrar, tooltips en los botones, Escape propio |
| 29 | Rendimiento | 6 | 8 | Canvas anidado, escrituras solo al cambiar, panel inactivo al cerrar; sin Profiler |

### Lo que queda abierto

- **Sin navegación por teclado.** Con la ventana abierta las flechas siguen moviendo al
  personaje; hacerlo bien pide un contexto de entrada propio de la ventana.
- **Sonido sintetizado.** El catálogo solo tiene `inv_open`; cuando haya clips grabados basta
  con rellenar los ids de `InventoryHudStyle`.
- **La tienda y el crafteo aún no reusan la casilla ni la tarjeta** (fase 4).
- **Sin búsqueda por nombre** y sin límite de peso (el juego no lo tiene).
- **Los objetos de caballero piden nivel 5**: antes se podían poner con cualquier nivel porque
  nada miraba el requisito. Es lo que dicen los datos, pero cambia el juego temprano.
