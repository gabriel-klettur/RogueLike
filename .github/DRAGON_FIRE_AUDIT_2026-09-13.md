# Auditoría del fuego del dragón — la decoración de la pantalla de carga

> Medida el 2026-09-13 a 1600x800 sobre el árbol de trabajo, con el arte enviado
> (`Resources/UI/Loading/background_ini.png`, 1536x1024). Todas las cifras salen de sondas sobre
> el código en ejecución, de capturas reales del juego y de lecturas del PNG de origen; ninguna
> está estimada. El método de las capturas es un **diff encendido/apagado** sobre el mismo
> fotograma: se monta la pintura como la monta la pantalla de carga, se fotografía con los cuatro
> hijos desactivados y se vuelve a fotografiar con ellos activos.

## 0. Qué es, en una frase

Cuatro piezas —brasas, fogonazo, humo y lavado de luz— dibujadas ENCIMA del penacho pintado y
ancladas a él en fracciones de la propia pintura; 797 líneas entre efecto, tabla de anclas y
fixture, sobre el pool de cuadriláteros que el menú ya tenía.

| Medida | Valor |
|---|---|
| Coste por tick | **0,0033 ms** |
| Asignaciones en 2000 ticks | **0 bytes** |
| Brasas en régimen | 36 de 96 (37 % del pool) |
| Humo en régimen | 7 de 24 (29 %) |
| Píxeles de pantalla que toca | **4,34 %** |
| De esos, los que SUMAN luz | 92,9 % (el 6,6 % restante es el humo) |
| Delta medio donde toca | 28,6 de 765 · máximo 268 |
| `Graphic`s nuevos | 4, todos `raycastTarget = false`, todos sobre una página de 64x32 |
| Tests | 10 |

## 1. Puntuación por eje

| Eje | Nota | Por qué |
|---|---:|---|
| Independencia de resolución | **10** | Colocación por anclas con offsets a cero: medido idéntico a cuatro tamaños (1600x1066, 3840x2560, 1024x683, 2560x1706) y circular a tres proporciones de pintura. No queda ningún píxel que pueda estar mal |
| Coste | **9,5** | 0,0033 ms/tick y **cero** asignaciones. El pool es un techo duro y se queda en el 37 % |
| Robustez | **9** | Paso de fotograma capado a 1/20 s, `unscaledDeltaTime`, arte sin medir dibuja NADA, fondo negro de reserva no arranca el efecto, teardown ramificado Play/Edit |
| Integración | **9** | Cuelga del `RectTransform` de la pintura, así que el orden de dibujo sale de la jerarquía y no hay ninguna decisión de sorting; el `CanvasGroup` de la pantalla lo funde gratis |
| Cobertura de tests | **8,5** | 10 tests, incluido el del fotograma de tres segundos y el de las cuatro resoluciones. No hay ninguno sobre el ASPECTO del resultado |
| Legibilidad de la interfaz | **8** | El texto sigue a 7,5-14,3 : 1. Pero **el 16,4 % de la energía del efecto cae en la banda de la barra** y el 5,6 % en la de la línea de estado |
| Lectura / dirección de arte | **7,5** | El fogonazo clava la boca y el chorro se mueve. Pero la rampa de brasas **no coincide con la pintura que anima** |
| Densidad | **6,5** | Las brasas tocan solo el **12,8 %** del núcleo pintado del chorro |
| Autoría | **4** | Solo las ANCLAS son dato. Tasas, colores, tamaños y tiempos son diez constantes privadas; no hay asset, ni editor, ni consola |
| Accesibilidad | **2** | `GameSettings.reduceMotion` existe, el menú y el título lo respetan, y `UI/Loading/` tiene **cero** referencias a él |
| Alcance | **2** | Una sola pintura medida. Nada más en el juego usa esta capa |

**Media: 7,0 / 10.**

## 2. Lo que está bien y por qué es barato

- **Cero asignaciones por fotograma.** Medido sobre 2000 ticks: el `GC.GetTotalMemory` no se
  mueve. Importa más aquí que en casi cualquier otro sitio del juego, porque esta pantalla corre
  mientras el hilo principal construye el mundo: una pausa de recolección cae justo encima de la
  barra que dice que todo va bien.
- **El pool no se llena.** 36 de 96 brasas y 7 de 24 de humo en régimen. Un emit por encima del
  techo se descarta, nunca se crece, así que el peor caso está acotado por construcción.
- **Una sola textura.** Las cuatro piezas dibujan de la misma página de 64x32, la bilineal que
  el título hornea. Sin ella, un punto de 2x2 del atlas del menú —filtrado a PUNTO a propósito—
  ampliado a unas unidades es un cuadrado duro, y unas decenas de cuadrados duros son grava.
- **Ninguna decisión de sorting.** Todo cuelga del rect de la pintura y uGUI dibuja en orden de
  jerarquía en profundidad, así que las piezas caen después del cuadro y antes de la barra y el
  texto. No hay ningún número de orden que mantener.

## 3. Los cuatro hallazgos que importan

### 3.1 La rampa de brasas no coincide con la pintura que anima (dirección de arte, 7,5)

Medido sobre el PNG, la llama entera de la boca a pasada la caída:

| Tramo | Tono | Saturación | Valor |
|---|---:|---:|---:|
| t = 0,00-0,25 | 34° | 0,88 | 0,84 |
| t = 0,25-0,50 | 33° | 0,89 | 0,86 |
| t = 0,50-0,75 | 31° | 0,94 | 0,84 |
| t = 0,75-1,00 | 30° | 0,98 | 0,85 |
| t = 1,00-1,40 | 27° | 0,99 | 0,81 |

La pintura **se enfría 7 grados** en todo su recorrido y **se satura** al alejarse. Los tonos
autorados:

| Constante | Tono | Saturación |
|---|---:|---:|
| `EmberHot` | 38° | **0,66** |
| `EmberCool` | **15°** | 0,93 |
| `MuzzleTint` | 27° | 0,83 |
| `WashTint` | 23° | 0,84 |

Dos desacuerdos concretos. La rampa de brasas recorre **23 grados contra los 7 de la pintura**, y
termina **12 grados más roja** que el arte en el punto donde cae el fuego. Y el extremo caliente
está **0,22 por debajo** en saturación: donde el cuadro pone naranja intenso, la brasa pone crema.

Que una brasa sea más caliente que la llama que la rodea es defendible —lo es de verdad— pero
esto no es eso: es una rampa construida sin mirar la referencia, y el error apunta en las dos
direcciones a la vez (más pálida al nacer, más roja al morir).

El núcleo más caliente del chorro, además, **no tiene gradiente ninguno**: tono 41 constante en
los cuatro cuartos, sat 0,88-0,91. Todo el enfriamiento vive en la llama exterior.

### 3.2 Un sexto de la energía del efecto cae sobre la interfaz (legibilidad, 8)

Del diff encendido/apagado, repartido por bandas de pantalla:

| Banda | Delta medio | Máximo | Parte de la energía |
|---|---:|---:|---:|
| Barra de progreso (y 0,76-0,82) | 5,15 | 74 | **16,4 %** |
| Línea de estado (y 0,70-0,75) | 4,52 | 49 | **5,6 %** |
| Consejo en cursiva (y 0,84-0,89) | 0,03 | 9 | 0,05 % |
| Feed derecho | 0,00 | 0 | 0,00 % |

El centroide de toda la energía está en **(0,349 · 0,663)**, que es prácticamente la altura de la
línea de estado: el lavado, que mide 0,164 del ancho, está centrado justo detrás de ella.

No es un defecto grave —el texto sigue midiendo entre **7,5 : 1** (el consejo) y **14,3 : 1** (la
línea de estado) en la captura real, porque los platos oscuros hacen su trabajo— pero es una
tendencia en la dirección equivocada: el efecto SUMA luz en el 93 % de lo que toca, y sumar luz
detrás de texto blanco solo puede bajar el contraste. Hoy sobra margen; el día que alguien suba
el lavado, lo primero que se degrada es lo único que el jugador necesita leer.

### 3.3 Las brasas tocan el 12,8 % del chorro que animan (densidad, 6,5)

Cruzando el núcleo pintado con el diff: **77 de 600 píxeles** del núcleo cambian. Una brasa es
discreta por definición y no debería tapar la pintura, pero un octavo es poco para que el ojo lea
el penacho entero como algo en movimiento; lo que se mueve hoy es sobre todo el tercio de la boca,
porque los nacimientos están amontonados ahí (`t = u²`).

### 3.4 Nada de esto es autorable, y no respeta reducir movimiento (autoría 4, accesibilidad 2)

`LoadingFireFX` lleva **diez constantes privadas** — dos tasas, cinco colores, el tope de paso y
los dos tamaños de pool — más los multiplicadores de colocación (`0,17`, `0,85`, `1,2`, `1,06`) y
los rangos de emisión escritos en las llamadas. Nada de eso tiene asset, ni entrada de editor, ni
comando de consola. Las ANCLAS sí son dato (`LoadingArtAnchors`), que es la mitad que más falta
hacía, pero afinar el efecto sigue siendo editar C# y recompilar — que es exactamente lo que costó
las dos iteraciones de esta misma sesión.

Y `GameSettings.reduceMotion` existe, el título y el menú lo respetan, y `grep` sobre
`Scripts/UI/Loading/` devuelve **cero**. Un jugador que ha pedido menos movimiento recibe 46
brasas por segundo y un fogonazo que late 1,82x cada 1,34 s.

## 4. Lo que ya se corrigió durante la construcción, y la medida que lo dijo

Se anotan porque las dos son reglas generales, no anécdotas.

- **Luz aditiva sobre pintura que YA es fuego no tiene nada que añadir: recorta a blanco.** La
  primera versión llevaba el fogonazo a `span * 0,42` y el lavado a `span * 1,30`, con alfas del
  doble. En la primera captura en vivo el fogonazo era una floración sobre el hocico del dragón y
  el lavado blanqueaba toda la izquierda del cuadro. Un acento sobre pintura clara tiene que ser
  pequeño y tenue o borra lo que acentúa. Está en `span * 0,17` y `span * 0,85`, y el lavado se
  centra en `Axis(1,2)` — PASADO el punto de caída, porque la punta es la pintura más brillante
  del lienzo e iluminarla es iluminar lo único que no lo necesita.
- **Un ancla normalizada no basta: la colocación tiene que ser por `anchorMin`/`anchorMax`.**
  Convertirla a unidades y escribir `anchoredPosition` + `sizeDelta` una vez fija ambos contra el
  rect que hubiera en ese momento. Medido antes del arreglo: el fogonazo se iba de (0,535 · 0,541)
  a **(0,515 · 0,517) a 3840x2560** y a **(0,555 · 0,564) a 1024x683**, con el diámetro entre el
  1,4 % y el 5,1 % del ancho, y el lavado se movía **un cuarto de la pintura**. Segundo fallo
  detrás del primero: `Attach` corre ANTES de la primera pasada de layout, así que el primer
  tamaño que veía no era el de la pintura — estaba mal desde el fotograma cero, no solo al
  redimensionar.

## 5. El latido, medido

`surge = 0,62 + 0,26·sin(4,7t) + 0,12·sin(11,3t + 1,7)`, recortado a [0,1].

| | |
|---|---|
| Recorrido | 0,240 a 1,000, media 0,620 |
| Saturado arriba | **0,09 %** del tiempo — lo justo para que el pico exista sin aplanarse |
| Alfa del fogonazo | 0,148 a 0,270 (**x1,82**) |
| Alfa del lavado | 0,069 a 0,130 (x1,88) |
| Periodos | 1,34 s y 0,56 s, razón **2,404** |

Dos senos a ritmos inconmensurables y no uno: un solo seno es un pulso, y un pulso se lee como una
lámpara con un contacto flojo. La razón 2,404 no es un racional simple, así que la forma de onda
no se repite de forma reconocible.

## 6. Coste de dibujo: un batch de más, por el ORDEN

Las cuatro piezas comparten textura, pero no material:

```text
FireWash    LoadingFireAdditive
FireSmoke   Default UI Material     <- rompe el batch
FireEmbers  LoadingFireAdditive
FireMuzzle  LoadingFireAdditive
```

uGUI agrupa recorriendo los hijos en orden y abre un batch nuevo cuando cambia el material, así
que la secuencia A-B-A-A son **tres batches**. Poniendo el humo el primero o el último serían dos.
El humo no puede compartir material —es la única pieza que QUITA luz, y en aditivo un píxel oscuro
no añade nada— así que la única palanca es el orden. Es un draw call sobre una pantalla que no
está haciendo nada más; se anota, no se corrige a ciegas, porque el orden actual está elegido
(luz, humo, brasas, fogonazo) y moverlo cambia qué tapa a qué.

## 7. Lo que NO se ha verificado

- Un redimensionado real de ventana **durante** una carga. Está cubierto estructuralmente (la
  colocación es por anclas) y por el guardia de `Tick`, y fijado por
  `TheDecoration_KeepsItsPlaceOnThePainting_AtEveryResolution`, pero nadie ha arrastrado la
  esquina de la ventana con la barra a medias.
- El efecto detrás del **panel de error** de la pantalla de carga. El fuego sigue ardiendo bajo
  él; probablemente esté bien, no se ha mirado.
- Nada en **PlayMode tests**. Los diez son EditMode.

## 8. Lo que haría, por orden de rendimiento

1. **Alinear la rampa de brasas con la pintura** (§3.1): subir `EmberHot` a saturación ~0,88 y
   subir `EmberCool` de 15° a ~27°. Es una edición de dos constantes y cierra el único desacuerdo
   medible entre el efecto y el arte sobre el que se dibuja.
2. **Respetar `reduceMotion`** (§3.4): sin brasas, sin goteo del latido, el fogonazo fijo en su
   valor medio. Es la nota más baja de la tabla y la más barata de subir.
3. **Sacar las constantes a un `LoadingFireStyle.asset`** (§3.4), con las tasas, los colores y los
   multiplicadores de colocación. Afinar esto costó dos ciclos de recompilar hoy.
4. **Repartir los nacimientos** (§3.3): `t = u²` amontona todo en la boca. Un exponente autorable
   —o simplemente `u^1,4`— sube la cobertura sin subir la tasa.
5. **Bajar el lavado o estrecharlo** (§3.2), para que la banda de la barra deje de recibir un
   sexto de la energía.
6. Un segundo arte de carga, que es lo que convertiría `LoadingArtAnchors` en una tabla en vez de
   en una fila.
