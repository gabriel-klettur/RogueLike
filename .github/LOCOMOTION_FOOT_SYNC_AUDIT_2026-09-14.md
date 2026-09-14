# Auditoría: sincronía de pasos (arte) con el movimiento

Fecha: 2026-09-14. Alcance: caminar y correr de los seis personajes jugables (`walk` y `chase`, lado `_e`) y cómo los mueve el código tras la skill Carrera. Método: medición sobre los PNG enviados (máscara de alfa por frame), hojas de contacto revisadas a ojo y lectura del código. Nada se ha cambiado todavía.

**Nota global: 4,2 / 10.** El sistema es sólido (impulso, energía, ritmo por velocidad, fase conservada), pero **los pies no pisan donde va el cuerpo**: el juego mueve a los personajes de 3 a 11 veces más rápido de lo que su arte dibuja, y hay dos defectos de bucle y orientación que se ven sin buscarlos.

---

## 1. Mediciones

Zancada estimada como la oscilación de la apertura de piernas en el 22 % inferior del cuerpo (máx − mín × 0,9). Un ciclo de 8 frames tiene 2 pasos, a 0,15 s por frame = 0,6 s por paso. Velocidad de juego: `basicSpeed` de la clase, × 1,35 en carrera a 0 % de skill.

| Personaje | Ciclo | Cuerpo (u) | Paso (u) | Arte (u/s) | Juego (u/s) | Patinaje | Alturas de cuerpo/s |
| --- | --- | --- | --- | --- | --- | --- | --- |
| dwarf | caminar | 1,81 | 0,24 | 0,40 | 4,00 | x10,0 | 2,2 |
| dwarf | correr | 1,77 | 0,60 | 1,01 | 5,40 | x5,4 | 3,1 |
| barbarian | caminar | 1,99 | 0,63 | 1,05 | 5,00 | x4,7 | 2,5 |
| barbarian | correr | 1,67 | 0,96 | 1,59 | 6,75 | x4,2 | 4,0 |
| elven | caminar | 2,56 | 0,71 | 1,19 | 6,00 | x5,1 | 2,3 |
| elven | correr | 2,61 | 1,79 | 2,98 | 8,10 | x2,7 | 3,1 |
| vampire | caminar | 2,70 | 0,54 | 0,91 | 7,00 | x7,7 | 2,6 |
| vampire | correr | 2,48 | 1,13 | 1,89 | 9,45 | x5,0 | 3,8 |
| mague | caminar | 2,66 | 0,54 | 0,91 | 5,00 | x5,5 | 1,9 |
| mague | correr | 2,65 | 0,61 | 1,02 | 6,75 | x6,6 | 2,6 |
| valkyrie | caminar | 1,81 | 0,60 | 1,01 | 7,00 | x6,9 | 3,9 |
| valkyrie | correr | 1,88 | 0,51 | 0,84 | 9,45 | x11,2 | 5,0 |

Referencias de oficio: una persona camina a ~0,8 alturas de cuerpo por segundo y corre a 2-3. **Aquí ya se camina a 1,9-3,9**: la velocidad de caminar del juego es una velocidad de carrera. El método es ruidoso en un 20-30 % (el arte generado no apoya los pies con precisión), pero el orden de magnitud es consistente en los doce ciclos.

Continuidad del bucle: similitud (IoU de alfa) del frame 7 con el 0 y con el 1. El animador salta el frame 0 en `Walk` y `Chase`.

| Ciclo | 7→0 | 7→1 | ¿Pertenece el 0 al bucle? |
| --- | --- | --- | --- |
| barbarian caminar / correr | 0,60 / 0,44 | 0,51 / 0,42 | sí / sí |
| elven correr | 0,58 | 0,35 | **sí, y es un apoyo** |
| mague correr | 0,46 | 0,43 | sí |
| valkyrie caminar / correr | 0,60 / 0,60 | 0,54 / 0,44 | sí / sí |
| dwarf caminar, vampire ambos, mague caminar | — | mayor | no (el salto es correcto) |

Hoja de contacto de la carrera del elfo: los frames 0 y 4 son los dos apoyos. Saltar el 0 deja un ciclo de 7 frames con un apoyo amputado, así que un pie pisa 0,15 s menos que el otro: **cojera** en cada ciclo.

---

## 2. Valoraciones

| # | Punto | Nota | Por qué |
| --- | --- | --- | --- |
| 1 | Zancada frente a velocidad (patinaje) | **2** | Patinaje de x2,7 a x11,2. Los pies deslizan de forma obvia en los seis. |
| 2 | Ritmo de animación ligado a la velocidad | **4** | El dial existe y funciona, pero sus referencias son derivadas (caminar = velocidad de clase), así que a paso normal el ritmo es 1: no corrige nada, solo evita empeorar al correr. |
| 3 | Orden del bucle (salto del frame 0) | **3** | Regla heredada de Python aplicada a todos los ciclos. En 7 de 12 el frame 0 pertenece al bucle; en la carrera del elfo es un apoyo. |
| 4 | Orientación frente a dirección de marcha | **2** | El ratón manda (`PlayerFacingResolver`, paso 1). Caminar alejándose del cursor reproduce el ciclo hacia delante mientras el cuerpo va hacia atrás: **moonwalk**. Además se corre de espaldas a velocidad completa. |
| 5 | Transición caminar↔correr | **7** | Velocidad sin escalón y fase conservada por fracción de ciclo. Falta casar por APOYO: los apoyos del walk y del run no caen en los mismos índices, así que puede cambiar el pie adelantado. |
| 6 | Arranque y parada | **4** | Arranca siempre en el frame 1 sea cual sea la pose, y al soltar salta a idle a mitad de zancada (pose con una pierna en el aire que desaparece en un frame). |
| 7 | Pisadas (polvo, ruido, marca) frente a apoyos del arte | **3** | `FootstepEmitter` emite por distancia (0,62 / 0,95 u). No sabe qué frame es un apoyo, así que el polvo sale entre pisadas y los segmentos de la marca del suelo no coinciden con el pie. |
| 8 | Línea de suelo, rebote y flotación | **6** | Suelo horneado correcto. Los frames aéreos de la carrera se elevan (hasta 23 px, correcto). Caminatas del mague y el elfo flotan 3-4 px en casi todo el ciclo. |
| 9 | Cobertura de direcciones (arte a 2 lados, movimiento a 8) | **4** | Moverse al norte o al sur muestra el perfil. Es aceptable en el género, pero con el patinaje se lee como deslizarse de lado. |
| 10 | Coherencia entre personajes | **4** | La valkyrie camina a 3,9 alturas/s y el mague a 1,9. Una misma pulsación mueve cuerpos con ritmos visuales muy distintos. |
| 11 | Variantes de ciclo (mague) | **6** | Rotan al entrar en el estado y nunca a mitad. Sin medir si comparten zancada. |
| 12 | Monstruos | **4** | Mismos problemas. El dial nuevo usa `speed` y `chasingSpeed` como referencia, así que tampoco corrige el patinaje de base. |
| 13 | Velocidad: aceleración y mezcla | **7** | Mezcla suave de 0,25 s y sin escalón. Caminar sigue siendo instantáneo. |
| 14 | Herramientas y verificación | **4** | `measure_stride.py` falló su primer intento y no hay test que ponga un tope al patinaje ni a la continuidad del bucle. |

---

## 3. Cómo quedaría profesional

Orden por impacto sobre lo que ve el jugador.

### 3.1 Datos de ciclo medidos por estado (arregla 1, 3, 5, 7 y 14)

Un bloque `LocomotionCycle` por ciclo en el manifiesto y en `EntityAssetConfig`:

- `loopStart`: 0 o 1, medido por continuidad del bucle en vez de una regla global.
- `contactFrames`: los índices de apoyo de cada pie (p. ej. `[0, 4]`).
- `strideUnits`: longitud de paso en unidades de mundo.

Lo propone una herramienta nueva (la sucesora de `measure_stride.py`, con detección de apoyo por el pie más bajo) y lo confirma una persona sobre una hoja de contacto con los apoyos marcados. Es el mismo reparto que `handTuned` en las bocas de hechizo.

### 3.2 Ritmo desde la zancada, con techo (arregla 1 y 2)

`rate = velocidad / (strideUnits × 2 / duración del ciclo)`, limitado a un techo de oficio (unos 14 fps en caminar, unos 18 en correr). Lo que el techo no absorbe es patinaje residual, y solo desaparece con la decisión 4.1.

### 3.3 Retroceso en vez de moonwalk (arregla 4)

Cuando la marcha y la mirada difieren más de ~100°:

- el ciclo se reproduce **al revés** (la reproducción inversa ya existe),
- la velocidad baja a ~70 %,
- no se acumula impulso de carrera.

Es el patrón de los twin-stick. Mirar al cursor sigue siendo la regla para apuntar.

### 3.4 Transición por apoyo (arregla 5)

Al pasar de caminar a correr, se salta al apoyo del MISMO pie en el ciclo nuevo, no a la misma fracción.

### 3.5 Parada y arranque asentados (arregla 6)

- **Parada**: al soltar, el ciclo avanza hasta el siguiente apoyo (como mucho 2 frames) antes de pasar a idle. El cuerpo ya está quieto; son 0,1-0,2 s de piernas.
- **Arranque**: empieza en el apoyo más cercano a la pose de idle.

### 3.6 Pisadas en el apoyo (arregla 7)

El animador emite un evento `FootContact(pie)` al mostrar un frame de `contactFrames`. De ahí salen el polvo, el ruido de `NoiseEvents` y el segmento de la marca del suelo, y dejan de medirse por distancia.

### 3.7 Suelo por frame (arregla 8)

La herramienta de 3.1 también mide cuántos píxeles flota cada frame de apoyo. Un desplazamiento `groundOffset` por frame lo corrige al hornear, sin retocar el arte.

### 3.8 Monstruos (arregla 12)

Los mismos datos, generados desde los manifiestos de wave13 y reutilizando la misma herramienta.

### 3.9 Tests de guarda (arregla 14)

- **Bucle**: `loopStart` coincide con la continuidad medida.
- **Patinaje**: a velocidad de caminar, `velocidad / (zancada × ritmo)` dentro de 0,8-1,25 una vez aplicado el techo.
- **Pisadas**: los eventos caen solo en `contactFrames`.

---

## 4. Decisiones que no son técnicas

1. **Velocidad base.** Con techos de fps razonables, el patinaje solo desaparece si caminar va más despacio: aproximadamente 55-65 % de la velocidad actual. Correr llegaría a la velocidad de hoy o algo más con la skill. Cambia el ritmo de todo el juego (distancias, persecuciones, dash).
2. **Retroceso.** O se reproduce al revés con penalización (recomendado), o el personaje gira hacia donde camina y el cursor solo apunta los hechizos.

Nota prevista si se hace todo con la decisión 1 aceptada: **~8,5 / 10**. Sin bajar la velocidad base: **~6,5 / 10**, porque el patinaje queda en x2-x4.

---

## 5. Después de implementar (2026-09-14)

Decisiones del usuario: caminar al ~60 % y retroceso mirando al cursor.

**Hecho:**

- `tools/atlas/locomotion_cycles.py` mide los 25 ciclos de caminar y correr (base, variantes y loadouts): `loopStart`, dos `contactFrames` a medio ciclo y `strideUnits`. Genera hojas de contacto marcadas, que se revisaron a ojo. Se importan con `Valkur > Players > Import Locomotion Cycles` a `Resources/Skills/LocomotionCycleCatalog.asset`, indexado por hoja.
- `DirectionalAnimator.Locomotion.cs`:
  - bucle medido;
  - evento `FootContact`;
  - reproducción inversa;
  - walk↔run casando el mismo pie;
  - arranque sobre un apoyo;
  - `FramesToNextContact` para la parada.
- `PlayerController.Locomotion.cs`:
  - ritmo desde la zancada con techo de 14/18 fps;
  - retroceso a >100° (70 % de la caminata, sin impulso);
  - parada que termina el paso (≤ 2 frames).
- `LocomotionTuning`:
  - caminar 0,6 de la velocidad de clase;
  - correr x1,0 → x1,3;
  - arranque 0,9 s → 0,3 s (~3 pasos → 1).
- `FootstepEmitter`: polvo, ruido y `StrideCompleted` salen del frame de apoyo cuando el ciclo está medido.

Patinaje estimado con los datos medidos y los techos de fps:

| | Antes (mediana) | Después (mediana) | Peor después |
| --- | --- | --- | --- |
| Todos los ciclos | x6,3 | **x1,8** | x3,6 (carrera de la valquiria al 100 %) |

**Notas nuevas:**

| # | Punto | Antes | Después |
| --- | --- | --- | --- |
| 1 | Zancada frente a velocidad | 2 | **7** |
| 2 | Ritmo ligado a velocidad | 4 | **8** |
| 3 | Orden del bucle | 3 | **9** |
| 4 | Orientación frente a marcha | 2 | **8** |
| 5 | Transición caminar↔correr | 7 | **9** |
| 6 | Arranque y parada | 4 | **8** |
| 7 | Pisadas frente a apoyos | 3 | **9** |
| 8 | Suelo y flotación | 6 | 6 |
| 9 | Cobertura de direcciones | 4 | 4 |
| 10 | Coherencia entre personajes | 4 | **6** |
| 11 | Variantes de ciclo | 6 | **8** |
| 12 | Monstruos | 4 | 4 |
| 13 | Aceleración | 7 | 7 |
| 14 | Herramientas y verificación | 4 | **8** |

**Global: 4,2 → 7,2.**

**Pendiente, y por qué:**

- **Suelo por frame (8):** los ciclos del mague con bastón y de la valquiria con escudo flotan 3-7 px. Exige un desplazamiento vertical por frame en el binder; no se tocó para no mover pivotes compartidos con el editor de bocas de hechizo.
- **Direcciones (9):** con arte a 2 lados, moverse al norte o al sur sigue mostrando el perfil. Solo lo arregla arte nuevo.
- **Monstruos (12):** falta ejecutar la herramienta sobre los manifiestos de wave13 (otro formato).
- **Carrera de la valquiria:** su zancada medida sale recortada (0,565 u) y queda en x2,8-x3,6. Revisar la hoja a ojo o subir `runMaxFps` solo si se ve bien.
