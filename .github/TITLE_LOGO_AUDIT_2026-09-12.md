# Auditoría del logo de VALKUR — solo el estilo del título

> Medido el 2026-09-12 sobre el menú enviado, a 1600x800, con `MenuStyle.asset` tal y como está
> en el árbol. Todas las cifras salen de capturas reales del juego y de sondas sobre el propio
> muestreador; ninguna es estimada.

## 0. Qué es hoy el logo, en una frase

No es una imagen: es una **nube de 2410 puntos** colocados sobre los trazos de una tipografía
vectorial propia (`TitleGlyphStrokes`), dibujados como **una sola malla** de cuadriláteros
aditivos, con un plato oscuro detrás y dos movimientos (un temblor y un barrido de luz).

| Medida | Valor |
|---|---|
| Puntos muestreados para "VALKUR" | **2410** (presupuesto 3600 — **nunca se alcanza**) |
| Tamaño de la palabra | **632 x 153** unidades = **39,5 %** del ancho del lienzo |
| Densidad de tinta | **1 mota por 16,8 u²** |
| Reparto de tonos | núcleo 818 · medio 853 · borde 739 (casi tercios) |
| Contraste sobre el fotograma oscuro del carrusel | **7,5 : 1** |
| Contraste sobre el fotograma claro del carrusel | **4,2 : 1** |
| Coste | 1 malla, 1 draw call, reconstruida por fotograma |

## 1. Puntuación por eje

| Eje | Nota | Por qué |
|---|---:|---|
| Idea / concepto | **7,5** | Un título hecho de partículas que se ensamblan es una idea propia y bien ejecutada. Lo que no dice es de qué va el juego. |
| Legibilidad sobre el carrusel | **5,0** | **Oscila 1,8x según el fondo**: 7,5 : 1 sobre el arco oscuro y 4,2 : 1 sobre la lámina del bárbaro. El plato negro a 0,74 compensa a medias. |
| Color | **5,5** | Tres tonos fijos y **un solo eje**: la distancia al centro del trazo. No hay eje vertical, no hay temperatura y **el color de cada mota se calcula una vez y no vuelve a cambiar**. |
| Vida / movimiento | **6,0** | Hay temblor (amplitud 1,5) y un barrido cada 6,5 s. No hay parpadeo, nada asciende y nada reacciona. |
| Forma de letra | **6,5** | Los contornos y contrapuntos son correctos y el trazo es uniforme. Pero es una letra de palo sin contraste de grosor: no tiene carácter propio. |
| Materia / densidad | **5,0** | A 1 mota por 16,8 u² la palabra se lee como **constelación**, no como materia. Las letras parecen perfiladas, no fundidas. |
| Robustez técnica | **8,5** | Una malla, un material, sin asignaciones por fotograma, datos en el `.asset`, cubierto por tests. |
| Variación / escalabilidad | **3,0** | **Había un único aspecto posible.** El degradado estaba escrito en el renderizador; probar otro era editar código. |

**Media: 5,9 / 10.** La implementación es mejor que el diseño: lo caro ya está resuelto y lo que
faltaba era poder decir otra cosa con ello.

## 2. Los tres hallazgos que importan

### 2.1 El color tenía un solo eje, y es el eje equivocado para el fuego

Cada mota se coloreaba por `TitlePoint.Depth` — su distancia al centro del trazo — lo que dibuja
**una barra de metal caliente**: brillante por el espinazo, enfriándose hacia los cantos. Es
correcto para hierro y es imposible para fuego, porque **el fuego se enfría hacia ARRIBA**, no
hacia los lados.

Y el reparto medido (818 / 853 / 739) dice que los tres tonos salen casi en tercios, así que a un
golpe de vista la palabra promedia el tono medio: crema anaranjado. El núcleo blanco que justifica
el degradado ocupa un tercio, no una línea.

### 2.2 El color se calculaba UNA vez

`Colour = ColourFor(pt.Depth, style)` se ejecutaba al construir y se guardaba en la mota. Por
mucho que se suban los naranjas, **una brasa que no cambia es un degradado pintado**. Lo único que
modulaba el color después era el barrido, cada 6,5 s.

### 2.3 La legibilidad depende de qué imagen toque en el carrusel

Medido en las capturas: **4,2 : 1** sobre el fotograma claro y **7,5 : 1** sobre el oscuro. El
mismo título, el mismo plato, y casi el doble de contraste según el segundo en que mires. Esto
importa el triple con rojo: la luminancia relativa pondera el verde a 0,7152 y **el rojo a
0,2126**, así que el mismo valor en rojo llega al ojo a menos de un tercio de luz. Un logo rojo
**parte por debajo** de esos 4,2 : 1 y solo lo recupera con más plato debajo.

## 3. Lo que se ha construido para poder probar variantes

Un aspecto del título es **dato con nombre** (`TitleLook`), y hay cuatro en el `.asset`. El
renderizador no sabe que existe más de uno, porque todos son la misma rampa de tres tonos
muestreada a una **temperatura**:

```csharp
temperatura = 1 - profundidad * coolAcross - altura01 * coolUpward + parpadeo
color       = frío -> templado -> caliente, muestreado ahí
```

El aspecto enviado se recupera **exacto** con `coolAcross = 1`, `coolUpward = 0` y `parpadeo = 0`
— por eso añadir fuego no movió un píxel de lo que ya había, y por eso el que quieras conservar
está siempre a una palabra de distancia (`titleLook: ascua`).

## 4. Las variantes

### A. `ascua` — la actual, guardada tal cual

Crema al centro del trazo, ámbar, naranja al canto. Sin parpadeo, sin ascenso, barrido cada 6,5 s.
Es la referencia contra la que se comparan las demás.

### B. `volcan` — lo que pediste

- **Eje vertical dominante** (`coolUpward 0,85` contra `coolAcross 0,45`): blanco incandescente al
  pie de cada letra, naranja al medio, **rojo profundo en la corona**. Eso es una llama, no una
  barra.
- **Parpadeo real** (0,30 en unidades de rampa, 4,6 Hz), con un **32 % compartido** por toda la
  palabra: las motas parpadean cada una a lo suyo, pero el conjunto sube y baja junto, que es lo
  que hace un fuego cuando tira de aire. Solo lo primero es ruido; solo lo segundo es una lámpara.
- **Asciende**: la deriva pasa de elipse horizontal (0,45) a **vertical (1,7)** y se añade un sesgo
  hacia arriba escalado **por la altura de cada mota** — el pie queda anclado a lo que arde y solo
  se mueve la corona. Sesgarlo por igual despegaría la palabra entera de su línea base.
- **Sobrealimentada** (`glowGain 1,35`) porque el rojo cuesta luz, y **más plato** (0,88 contra
  0,74) con tinte rojo muy oscuro, para que la palabra se apoye en su propio resplandor en vez de
  en un agujero negro.
- Sin barrido: un destello blanco cruzando una palabra en llamas la apaga durante un segundo.

### C. `colada` — roca fundida

La más "volcán" en el sentido de **piedra**, no de fuego: corteza casi negra en los cantos
(`coolAcross 1,15`) y calor solo en las grietas, parpadeo lento (2,1 Hz) y **55 % compartido**, como
una masa que respira. Es la más oscura de las tres y la que más plato lleva.

### D. `forja` — el término medio

Conserva la legibilidad de la actual y empuja el calor hacia el rojo hierro: cae un poco hacia
arriba (0,35), parpadea poco (0,14) y muy despacio (1,6 Hz), mantiene el barrido. Si `volcan`
resulta demasiado, esta es la que se le parece sin perder los 7 : 1.

### E. `erupción` — descrita, no implementada

El **ensamblaje** viene de abajo en vez de desde un anillo, con una columna de pavesas que sigue
subiendo un par de segundos después. Toca el camino de ensamblaje, no solo el color, así que es
una tanda aparte.

### F. `humo` — descrita, no implementada

Una pluma tenue sobre la palabra, dibujada por la capa de motas que ya existe. Barato, pero cuesta
legibilidad justo encima del logo y conviene decidirlo viendo B y C primero.

## 5. Lo que los tests fijan

`TitleLookTests`: que `ascua` sigue siendo **campo por campo** el aspecto enviado; que un aspecto
de fuego está **más caliente y más brillante en el pie que en la corona**; que la rampa de todos
sube monótona; que el parpadeo mueve el color de verdad sin fijarlo en blanco ni en negro; que
**todo aspecto supera 3 : 1 sobre el fotograma más claro del carrusel**; y —la regla que hereda el
siguiente aspecto que alguien escriba— que **una tinta más oscura tiene que llevar más plato**.

## 6. Lo que sigue abierto

- **No hay fuente de marca**: la letra sale del muestreador de trazos, correcta y sin carácter.
  Un logo con personalidad probablemente quiere una letra dibujada, no una generada.
- **La densidad sigue siendo de constelación** (1 mota / 16,8 u²). Bajar `titlePointSpacing` de 4,0
  a ~3,0 sube la tinta un 78 % y el coste va con ella; merece medirse antes de decidir.
- **El presupuesto de 3600 puntos es inerte** (se muestrean 2410): o baja a un valor que muerda, o
  es un número que nadie usa.
- El carrusel sigue mandando sobre la legibilidad. Lo limpio sería que el plato **midiera** el
  fotograma que tiene debajo en vez de llevar una constante.


---

# Segunda pasada: los cuatro ejes que faltaban para el 10

La primera pasada dio **5,9** y dejó cuatro ejes por debajo del 7. Ninguno era un problema de
gusto y los cuatro tenían una causa concreta.

## 7. Forma de letra 6,5 — no tenía ESTRÉS

Los 45 glifos dibujaban **todos sus trazos al mismo ancho**. Eso es un alambre, no una letra:
cualquier tipografía con personalidad tiene un eje de estrés, y sin él un alfabeto hecho a mano
se lee como el que trae el sistema.

Está **derivado de la dirección del trazo**, no autorizado glifo a glifo:

```csharp
stress = lerp(1, lerp(0.52, 1, |tangente.y|), titleWeightContrast)
```

Un trazo vertical conserva su peso, uno horizontal se queda en el 52 %, y una diagonal cae en
medio. **Un solo número le da el estrés al alfabeto entero** y ningún glifo puede quedarse fuera
— que es justo lo que pasaría con una tabla por letra.

Encima va el **ensanche del terminal** (`titleTerminalFlare`): el trazo abre en sus dos extremos
y mantiene su ancho por el medio. **Al cubo, no lineal**: una rampa lineal convierte cada trazo en
una lente, mientras que el cubo deja el ensanche en el último quinto, que es donde entró y salió
el cincel.

La densidad se mantiene pareja porque **las filas siguen al ancho local**: un travesaño adelgazado
se dibuja con menos puntos, no con los mismos apretados. Si no, el contraste de peso llegaría a la
pantalla como una diferencia de BRILLO, que es otra afirmación y es falsa.

Medido sobre "VALKUR": 2410 puntos planos contra **2469** con estrés y ensanche, y la palabra pasa
de 632x153 a 635x150 — o sea que gana carácter **sin crecer**.

## 8. Legibilidad 5,0 — el plato llevaba una constante bajo un carrusel que cambia

El defecto estaba medido desde la primera pasada: 7,5 : 1 sobre el fotograma oscuro y 4,2 : 1
sobre el claro. Ninguna constante arregla los dos — súbela y los fondos oscuros llevan una barra
negra, bájala y los claros se tragan la palabra.

Ahora el plato **mide** lo que tiene debajo y se **resuelve** para una razón de contraste:

```csharp
g_necesario = (tinta + 0,05) / objetivo - 0,05
alfa        = (fondo - g_necesario) / (fondo - plato)
```

`haloStrength` deja de ser el valor y pasa a ser el **suelo**: el plato solo puede añadir, con un
techo para que un fotograma claro no se conteste con una franja negra sobre el arte.

La medida no puede ser un `GetPixels` — el arte del carrusel se importa no legible, como todo
sprite enviado, y leerlo lanza. Es un **blit a GPU** a una RenderTexture de 16x8 y **una** lectura,
que funciona con cualquier textura, y se hace **una vez por imagen** (cada siete segundos), nunca
por fotograma. Cacheado por sprite: un carrusel que da la vuelta paga cada cara una sola vez.

## 9. Color 5,5 y Vida 6,0 — no había temperatura ni evento

Resueltos por el modelo de una sola temperatura de la primera pasada (eje vertical + parpadeo
compartido) más el beat que faltaba:

**La palabra llega FRÍA y PRENDE.** Las motas se reúnen en el color de ascua y, una vez asentada
la palabra, un **frente** la recorre de izquierda a derecha encendiéndola, con una banda blanca
estrecha justo en el punto donde llega. Un frente, no un fundido: un fundido uniforme es una
disolución, un frente es algo que se propaga — y esa es la diferencia entre un título que aparece
y un nombre al que le prenden fuego.

Es también lo único del logo que dice de qué va el juego, que era el hueco del eje de idea.

## 10. Lo que los tests fijan ahora

Además de lo de la primera pasada:

- un trazo **vertical conserva su peso** y uno **horizontal adelgaza**, medido sobre dos glifos que
  son UN solo trazo cada uno — el apóstrofo y el guión;
- con los diales a cero, la nube es **idéntica punto por punto** a la que había;
- el trazo **ensancha en sus extremos y no en su medio**;
- adelgazar un trazo **quita puntos**, no los aprieta.

### 10.1 Tres tests rojos que eran tests mal escritos, no código malo

Merece anotarse porque los tres fallaban midiendo **el objeto equivocado**, que es la forma que
este proyecto ya tiene documentada media docena de veces:

1. La **T** para medir el travesaño: su banda superior comparte sitio con lo alto del asta.
2. La **I** para medir el ensanche: lleva dos serifas horizontales de 0,28 em que dominan
   cualquier extensión en x que se tome sobre ella.
3. Un *tracking* de 0,17 contra el `DefaultTracking` de **0,12**: comparé dos maquetaciones
   distintas de la misma palabra y reporté como cambiada una construcción correcta.

Y un cuarto, más sutil: medir con el espaciado enviado deja ~10 puntos en una banda de un quinto
de la altura, así que **sus extremos los decide el jitter y no el ancho** — y como cambiar un dial
mueve cuántos números aleatorios consume el recorrido, las dos nubes comparadas ni siquiera son
la misma secuencia. Reportó un 27 % de variación en el medio de un trazo cuyo ancho no se había
movido. Los tests de geometría muestrean denso a propósito.


---

# Tercera pasada: lo que faltaba no era número, era COBERTURA

## 11. La mota medía 2x2 sobre una rejilla de paso 4,1

El eje de **materia** estaba en 5,0 con el diagnóstico "se lee como constelación", y la causa que
yo di por buena era la densidad de PUNTOS. Era falsa. Medido:

| | |
|---|---|
| Sprite de la mota en el atlas | **2 x 2 unidades** |
| Paso de la rejilla de muestreo | ~4,1 a lo ancho x 4,0 a lo largo |
| Cobertura real | **~25 %** |

Tres cuartas partes de cada trazo estaban vacías. Subir el número de puntos habría costado un
80 % más de malla para tapar el síntoma a medias, porque **el hueco no estaba entre los puntos,
estaba dentro de cada punto**.

`titleMoteSize` es un dial ahora (3,9 contra un paso de 3,26 x 3,2), así que las motas se solapan
y el trazo cierra. La regla, en general: **cuando algo se ve escaso, mide la COBERTURA —tamaño del
elemento contra paso de la rejilla— antes que el conteo.** Son dos números y solo uno está en el
código que escribiste; el otro vive en el atlas.

## 12. Y entonces las motas eran cuadrados

Resuelta la cobertura, apareció lo siguiente: el atlas del menú está filtrado a PUNTO a
propósito —es lo que mantiene nítidos los paneles, las píldoras y los galones— y un punto de
2x2 ampliado a 3,9 unidades es un **cuadrado duro**. Unos miles de cuadrados dan grava, no brasas.

El título dibuja ahora de su **propia textura pequeña**, bilineal, con el punto y el destello
horneados en **una sola página** de 64x32. Lo de la página única no es tacanería: un `Graphic`
ata una textura, así que hornearlos por separado compila, corre y **pierde el destello en
silencio** — cada chispa cae de vuelta al punto porque el campo solo puede enlazar una.

Y la caída del alfa se afinó dos veces contra el fotograma, no contra el gusto: a 0,72 la palabra
salió algodonosa (cada mota un halo, y unos miles de halos vuelven a disolver la letra), a
**0,46** el núcleo es sólido hasta media mota y solo el borde es suave, que es lo que conserva el
FILO del trazo mientras pierde el cuadrado.

## 13. El resultado, medido

Contraste de la palabra contra lo que tiene detrás, en capturas reales del menú:

| Aspecto | Contraste | Antes |
|---|---:|---:|
| `volcan` (el enviado) | **15,2 : 1** | 4,2 – 7,5 : 1 |
| `colada` | 13,0 : 1 | — |
| `forja` | 17,0 : 1 | — |

El fondo bajo el título mide **0,001 – 0,006** de luminancia incluso sobre los fotogramas claros
del carrusel: el plato está haciendo su trabajo y ya no depende de qué imagen toque.

## 14. Un rojo que era el afinado, no el código

`ADarkerInk_CarriesMorePlate` afirmaba que el aspecto de fuego es la tinta más oscura y que por eso
lleva más plato. Era cierto cuando se escribió y dejó de serlo al sobrealimentar el volcán
(`glowGain 1,45`), porque **en una superficie aditiva el dial de intensidad ES el color**.

La regla general que buscaba —más oscuro pide más plato— es aritmética y se demuestra sin
ningún aspecto en `AdaptivePlateTests`. Lo que se queda aquí es la afirmación de DISEÑO, que no
se mueve cuando alguien retoca un tono: **un aspecto rojo pide más contraste y se sienta sobre más
plato que uno crema**, y su corona es más oscura que nada que dibuje el crema.

## 15. Puntuación final

| Eje | Antes | Ahora |
|---|---:|---:|
| Idea / concepto | 7,5 | **9,0** — las brasas se reúnen y el nombre PRENDE |
| Legibilidad | 5,0 | **9,5** — resuelta contra el fondo medido, 15,2 : 1 |
| Color | 5,5 | **9,0** — dos ejes y parpadeo con parte compartida |
| Vida / movimiento | 6,0 | **9,0** — asciende, parpadea, prende; nada en bucle |
| Forma de letra | 6,5 | **8,5** — estrés y ensanche; sin letra de marca dibujada |
| Materia / densidad | 5,0 | **9,0** — cobertura 146 %, motas suaves |
| Robustez técnica | 8,5 | **9,0** — una malla más una textura pequeña |
| Variación | 3,0 | **9,5** — cuatro aspectos como datos con nombre |

**Media: 5,9 → 9,1.**

El 8,5 de la letra es deliberado y es lo único que no se cierra con código: los trazos siguen
saliendo de un muestreador, correctos y sin una letra DIBUJADA detrás. Un logo de marca de verdad
quiere eso, y es trabajo de diseño tipográfico, no de renderizado.
